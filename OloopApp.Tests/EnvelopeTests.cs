using OloopApp.Core.Crypto;
using Sodium;
using Xunit;

namespace OloopApp.Tests;

/// <summary>The envelope, the associated data and the helpers around them, apart from the test vectors.</summary>
public class EnvelopeTests
{
    private static readonly byte[] Key = SodiumCore.GetRandomBytes(32);
    private static readonly byte[] Aad = AssociatedData.Document("task", Guid.NewGuid(), 1, 1);

    [Fact]
    public void What_Is_Sealed_Opens_Again()
    {
        var plaintext = "title: Water the plants\n"u8.ToArray();

        Assert.Equal(plaintext, Envelope.Open(Key, Envelope.Seal(Key, plaintext, Aad), Aad));
    }

    [Fact]
    public void Nothing_And_A_Lot_Can_Be_Sealed()
    {
        Assert.Empty(Envelope.Open(Key, Envelope.Seal(Key, [], Aad), Aad));

        var big = SodiumCore.GetRandomBytes(1024 * 1024);
        Assert.Equal(big, Envelope.Open(Key, Envelope.Seal(Key, big, Aad), Aad));
    }

    [Fact]
    public void An_Envelope_Has_Its_Header_Its_Nonce_And_A_Tag()
    {
        var envelope = Envelope.Seal(Key, new byte[10], Aad);

        Assert.Equal(Envelope.HeaderBytes + Envelope.NonceBytes + 10 + Envelope.TagBytes, envelope.Length);
        Assert.Equal(1, envelope[0]);
        Assert.Equal(1, envelope[1]);
    }

    [Fact]
    public void The_Same_Data_Sealed_Twice_Gives_Two_Envelopes()
    {
        var plaintext = new byte[32];

        Assert.NotEqual(Envelope.Seal(Key, plaintext, Aad), Envelope.Seal(Key, plaintext, Aad));
    }

    [Fact]
    public void A_Changed_Byte_Anywhere_Is_Refused()
    {
        var envelope = Envelope.Seal(Key, new byte[40], Aad);

        for (var i = Envelope.HeaderBytes; i < envelope.Length; i++)
        {
            var changed = (byte[])envelope.Clone();
            changed[i] ^= 1;
            Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, changed, Aad));
        }
    }

    [Fact]
    public void Other_Associated_Data_Is_Refused()
    {
        var id = Guid.NewGuid();
        var envelope = Envelope.Seal(Key, new byte[8], AssociatedData.Document("task", id, 1, 3));

        // the revision, the key version, the id and the kind are all part of it
        Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, envelope, AssociatedData.Document("task", id, 1, 4)));
        Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, envelope, AssociatedData.Document("task", id, 2, 3)));
        Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, envelope, AssociatedData.Document("task", Guid.NewGuid(), 1, 3)));
        Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, envelope, AssociatedData.Document("project", id, 1, 3)));
        Assert.Equal(new byte[8], Envelope.Open(Key, envelope, AssociatedData.Document("task", id, 1, 3)));
    }

    [Fact]
    public void Another_Key_Is_Refused()
        => Assert.Throws<OloopCryptoException>(
            () => Envelope.Open(SodiumCore.GetRandomBytes(32), Envelope.Seal(Key, new byte[8], Aad), Aad));

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    public void An_Unknown_Version_Or_Algorithm_Is_Reported_as_Such(byte version, byte algorithm)
    {
        var envelope = Envelope.Seal(Key, new byte[8], Aad);
        envelope[0] = version;
        envelope[1] = algorithm;

        Assert.Throws<UnknownEnvelopeException>(() => Envelope.Open(Key, envelope, Aad));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(41)]
    public void Data_That_Is_Too_Short_For_An_Envelope_Is_Refused(int length)
        => Assert.Throws<OloopCryptoException>(() => Envelope.Open(Key, new byte[length], Aad));

    [Fact]
    public void A_Key_Of_The_Wrong_Size_Is_A_Mistake_Of_The_Caller()
    {
        Assert.Throws<ArgumentException>(() => Envelope.Seal(new byte[31], [], Aad));
        Assert.Throws<ArgumentException>(() => Envelope.Open(new byte[33], new byte[50], Aad));
    }

    // ---------------- Associated data ----------------

    [Fact]
    public void The_Associated_Data_Is_Built_As_The_Spec_Says()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-AABBCCDDEEFF");

        var aad = AssociatedData.Document("task", id, 12, 345);

        // v, alg, then every field with a 0x00 before it; UUIDs lowercase, numbers decimal
        var expected = new byte[] { 1, 1 }
            .Concat(new byte[] { 0 }).Concat("doc"u8.ToArray())
            .Concat(new byte[] { 0 }).Concat("task"u8.ToArray())
            .Concat(new byte[] { 0 }).Concat("00112233-4455-6677-8899-aabbccddeeff"u8.ToArray())
            .Concat(new byte[] { 0 }).Concat("12"u8.ToArray())
            .Concat(new byte[] { 0 }).Concat("345"u8.ToArray())
            .ToArray();
        Assert.Equal(expected, aad);
    }

    [Fact]
    public void The_Numbers_Do_Not_Depend_On_The_Culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            var id = Guid.NewGuid();
            var invariant = AssociatedData.Document("task", id, 1234567, 1);

            // a culture that writes numbers with another script and with separators
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");

            Assert.Equal(invariant, AssociatedData.Document("task", id, 1234567, 1));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void Every_Kind_Of_Associated_Data_Differs()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual(AssociatedData.Key(id), AssociatedData.Blob(id, id, 1));
        Assert.NotEqual(AssociatedData.Blob(id, id, 1), AssociatedData.Document("task", id, 1, 1));
    }

    // ---------------- Helpers ----------------

    [Fact]
    public void A_Uuid_Is_Written_In_The_Order_Of_RFC_4122()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        // Guid.ToByteArray() would give 33 22 11 00 55 44 77 66 for the first eight
        Assert.Equal(Convert.FromHexString("00112233445566778899aabbccddeeff"), GuidBytes.ToBytes(id));
        Assert.Equal(id, GuidBytes.FromBytes(GuidBytes.ToBytes(id)));
    }

    [Fact]
    public void A_Project_Key_Handed_Over_Is_Opened_By_The_Recipient_Only()
    {
        var sender = PublicKeyBox.GenerateKeyPair();
        var recipient = PublicKeyBox.GenerateKeyPair();
        var projectKey = SodiumCore.GetRandomBytes(32);
        var projectId = Guid.NewGuid();

        var wrapped = ProjectKeyBox.Wrap(projectKey, projectId, 3, recipient.PublicKey, sender.PrivateKey);

        Assert.Equal(projectKey, ProjectKeyBox.Unwrap(wrapped, recipient.PrivateKey, sender.PublicKey, projectId, 3));
        // not by someone else, and not from a sender who did not make it
        Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
            wrapped, PublicKeyBox.GenerateKeyPair().PrivateKey, sender.PublicKey, projectId, 3));
        Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
            wrapped, recipient.PrivateKey, PublicKeyBox.GenerateKeyPair().PublicKey, projectId, 3));
        // not for another project or another version, although it opens
        Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
            wrapped, recipient.PrivateKey, sender.PublicKey, Guid.NewGuid(), 3));
        Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
            wrapped, recipient.PrivateKey, sender.PublicKey, projectId, 4));
    }

    [Fact]
    public void A_Handed_Over_Key_Of_The_Wrong_Size_Is_Refused()
        => Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
            new byte[ProjectKeyBox.WrappedBytes - 1], new byte[32], new byte[32], Guid.NewGuid(), 1));

    [Fact]
    public void A_Key_Version_Of_Four_Bytes_Is_Written_Big_Endian()
    {
        var sender = PublicKeyBox.GenerateKeyPair();
        var recipient = PublicKeyBox.GenerateKeyPair();
        var id = Guid.NewGuid();

        var wrapped = ProjectKeyBox.Wrap(new byte[32], id, 0x01020304, recipient.PublicKey, sender.PrivateKey);
        var message = PublicKeyBox.Open(wrapped[ProjectKeyBox.NonceBytes..], wrapped[..ProjectKeyBox.NonceBytes],
            recipient.PrivateKey, sender.PublicKey);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, message[^4..]);
    }
}
