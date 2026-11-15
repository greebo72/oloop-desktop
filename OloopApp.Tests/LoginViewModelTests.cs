using System.Net;
using System.Text;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

public class LoginViewModelTests
{
    private readonly FakeOloopServer _server = new();

    private LoginViewModel NewViewModel(string user = "timo")
        => new(new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl), FakeOloopServer.BaseUrl, user, "Test Mac");

    [Fact]
    public void Sign_In_Needs_A_Password_And_A_Device_Name()
    {
        var vm = NewViewModel();
        Assert.False(vm.CanSignIn);

        vm.Password = "secret";
        Assert.True(vm.CanSignIn);

        vm.DeviceName = "  ";
        Assert.False(vm.CanSignIn);
    }

    [Fact]
    public void The_Device_Name_Defaults_To_The_Name_Of_The_Computer()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        Assert.Equal(Environment.MachineName, new LoginViewModel(api, FakeOloopServer.BaseUrl, "timo").DeviceName);
    }

    [Fact]
    public async Task A_Right_Password_Signs_In_And_The_Password_Is_Handed_Over_Once()
    {
        var vm = NewViewModel();
        vm.Password = "secret";

        Assert.True(await vm.SignInAsync());
        Assert.Null(vm.Error);

        Assert.Equal("secret", vm.TakePassword());
        Assert.Equal("", vm.Password);
    }

    [Fact]
    public async Task A_Wrong_Password_Gives_One_Message_For_User_And_Password()
    {
        var vm = NewViewModel();
        vm.Password = "wrong";
        Assert.False(await vm.SignInAsync());
        var wrongPassword = vm.Error;

        var unknown = NewViewModel("nobody");
        unknown.Password = "secret";
        Assert.False(await unknown.SignInAsync());

        Assert.Contains("wrong", wrongPassword);
        Assert.Equal(wrongPassword, unknown.Error); // the server does not tell which of the two was wrong
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_Server_That_Is_Down_Is_Reported_And_A_Retry_Is_Possible()
    {
        var vm = NewViewModel();
        vm.Password = "secret";

        _server.Down = true;
        Assert.False(await vm.SignInAsync());
        Assert.False(string.IsNullOrEmpty(vm.Error));
        Assert.True(vm.CanSignIn);

        _server.Down = false;
        Assert.True(await vm.SignInAsync());
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Too_Many_Attempts_Say_How_Long_To_Wait()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"code":"rate_limited","message":"slow down"}}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(150));
            return response;
        });
        var vm = new LoginViewModel(new ServerApi(new HttpClient(handler), FakeOloopServer.BaseUrl), FakeOloopServer.BaseUrl, "timo", "Mac");
        vm.Password = "x";

        Assert.False(await vm.SignInAsync());
        Assert.Contains("3 minutes", vm.Error);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(answer(request));
    }
}
