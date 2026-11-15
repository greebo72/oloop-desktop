using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OloopApp.Core.Crypto;
using Sodium;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The test vectors of the server backend (spec/fixtures/server/vectors.json). They
/// were made with another implementation (PyNaCl), so every test here is a
/// comparison with it: the same bytes in, the same bytes out, and what has to be
/// refused is refused.
/// </summary>
public class ServerCryptoVectorsTests
{
    private static readonly JsonElement Root = Load();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "server", "vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static byte[] Hex(JsonElement e, string name) => Convert.FromHexString(e.GetProperty(name).GetString()!);

    private static IEnumerable<JsonElement> Cases(JsonElement parent) => parent.GetProperty("cases").EnumerateArray();

    private static Guid Id(JsonElement e, string name) => Guid.Parse(e.GetProperty(name).GetString()!);

    private static JsonElement Keys => Root.GetProperty("keys");
    private static JsonElement Agents => Root.GetProperty("agents");

    private static byte[] UserSecret => Hex(Keys, "secretKeyHex");
    private static byte[] UserPublic => Hex(Keys, "publicKeyHex");

    /// <summary>The secret key that belongs to each public key of the vectors.</summary>
    private static byte[] SecretOf(byte[] publicKey)
    {
        foreach (var (pub, sec) in new[]
        {
            (UserPublic, UserSecret),
            (Hex(Agents.GetProperty("key1"), "publicKeyHex"), Hex(Agents.GetProperty("key1"), "secretKeyHex")),
            (Hex(Agents.GetProperty("key2"), "publicKeyHex"), Hex(Agents.GetProperty("key2"), "secretKeyHex")),
        })
        {
            if (pub.AsSpan().SequenceEqual(publicKey))
                return sec;
        }
        throw new InvalidOperationException("a public key that the vectors do not have a secret key for");
    }

    // ---------------- Passphrase to key ----------------

    [Fact]
    public void Argon2id_Gives_The_Key_Of_The_Vectors()
    {
        foreach (var c in Cases(Root.GetProperty("kdf")))
        {
            var key = KeyDerivation.DerivePassphraseKey(
                Passphrase.ToKeyBytes(c.GetProperty("passphrase").GetString()!),
                Hex(c, "saltHex"), c.GetProperty("m").GetInt64(), c.GetProperty("t").GetInt64());

            Assert.Equal(Hex(c, "keyHex"), key);
        }
    }

    // ---------------- From typed text to bytes ----------------

    /// <summary>Text from code points, so that nothing changes how a vector is spelled. A surrogate half stays a lone char.</summary>
    private static string TextOf(JsonElement codePoints)
    {
        var text = new StringBuilder();
        foreach (var point in codePoints.EnumerateArray())
        {
            var cp = point.GetInt32();
            text.Append(cp is >= 0xD800 and <= 0xDFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp));
        }
        return text.ToString();
    }

    private static JsonElement PassphraseEncoding => Root.GetProperty("kdf").GetProperty("encoding");

    [Fact]
    public void Typed_Text_Becomes_The_Utf8_Bytes_Of_The_Vectors_Without_Normalization()
    {
        foreach (var c in PassphraseEncoding.GetProperty("cases").EnumerateArray())
        {
            var bytes = Passphrase.ToKeyBytes(TextOf(c.GetProperty("codePoints")));

            Assert.True(Hex(c, "utf8Hex").SequenceEqual(bytes), c.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void The_Two_Spellings_Of_An_Umlaut_Are_Two_Passphrases()
    {
        var keys = PassphraseEncoding.GetProperty("keys");
        var derived = new List<byte[]>();

        foreach (var c in keys.GetProperty("cases").EnumerateArray())
        {
            var key = KeyDerivation.DerivePassphraseKey(
                Passphrase.ToKeyBytes(TextOf(c.GetProperty("codePoints"))),
                Hex(keys, "saltHex"), keys.GetProperty("m").GetInt64(), keys.GetProperty("t").GetInt64());

            Assert.Equal(Hex(c, "keyHex"), key);
            derived.Add(key);
        }

        Assert.Equal(2, derived.Count);
        Assert.NotEqual(derived[0], derived[1]);
    }

    [Fact]
    public void Text_That_Is_Not_Valid_Unicode_Is_Refused()
    {
        var refused = PassphraseEncoding.GetProperty("refused").EnumerateArray().ToList();

        Assert.NotEmpty(refused);
        foreach (var c in refused)
        {
            var text = TextOf(c.GetProperty("codePoints"));
            Assert.Throws<OloopCryptoException>(() => Passphrase.ToKeyBytes(text));
        }
    }

    [Fact]
    public void An_Emoji_Is_Four_Bytes_Not_Two_Surrogate_Halves()
        => Assert.Equal(Convert.FromHexString("F09F9491"), Passphrase.ToKeyBytes("\U0001F511"));

    [Fact]
    public void An_Empty_Text_Gives_No_Bytes_The_Caller_Decides_Whether_That_Is_Allowed()
        => Assert.Empty(Passphrase.ToKeyBytes(""));

    [Fact]
    public void The_Policy_Of_The_Vectors_Is_Applied()
    {
        var policy = Root.GetProperty("kdf").GetProperty("policy");
        Assert.Equal(policy.GetProperty("minM").GetInt64(), KeyDerivation.MinMemoryBytes);
        Assert.Equal(policy.GetProperty("minT").GetInt64(), KeyDerivation.MinPasses);

        foreach (var c in policy.GetProperty("cases").EnumerateArray())
        {
            var m = c.GetProperty("m").GetInt64();
            var t = c.GetProperty("t").GetInt64();
            if (c.GetProperty("accept").GetBoolean())
                KeyDerivation.CheckPolicy(m, t);
            else
                Assert.Throws<OloopCryptoException>(() => KeyDerivation.CheckPolicy(m, t));
        }
    }

    [Fact]
    public void More_Memory_Than_Libsodium_Takes_Is_Refused()
    {
        KeyDerivation.CheckPolicy(uint.MaxValue, 2);
        Assert.Throws<OloopCryptoException>(() => KeyDerivation.CheckPolicy(uint.MaxValue + 1L, 2));
    }

    [Fact]
    public void A_Salt_Of_The_Wrong_Size_Is_Refused()
        => Assert.Throws<OloopCryptoException>(() => KeyDerivation.DerivePassphraseKey(
            [1], new byte[15], KeyDerivation.MinMemoryBytes, KeyDerivation.MinPasses));

    // ---------------- The key bundle ----------------

    [Fact]
    public void The_Bundle_Of_The_Vectors_Is_Read()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());

        Assert.Equal(UserPublic, bundle.PublicKey);
        Assert.Equal(Hex(Keys, "wrappedSecretKeyHex"), bundle.WrappedSecretKey);
        Assert.Equal(Hex(Keys, "recoveryWrappedSecretKeyHex"), bundle.RecoveryWrappedSecretKey);
        Assert.Equal(64L * 1024 * 1024, bundle.MemoryBytes);
        Assert.Equal(3, bundle.Passes);
        Assert.Equal(Hex(Cases(Root.GetProperty("kdf")).First(), "saltHex"), bundle.Salt);
    }

    [Fact]
    public void The_Bundle_Is_Written_As_The_Api_Carries_It()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());

        using var written = JsonDocument.Parse(bundle.ToJson());
        using var expected = JsonDocument.Parse(Keys.GetProperty("bundle").GetRawText());

        foreach (var property in expected.RootElement.EnumerateObject())
        {
            Assert.True(written.RootElement.TryGetProperty(property.Name, out var value), property.Name);
            Assert.Equal(property.Value.ToString(), value.ToString());
        }
        Assert.Equal(expected.RootElement.EnumerateObject().Count(), written.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void The_Passphrase_Key_Opens_The_Secret_Key()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());
        var passphrase = Root.GetProperty("kdf").GetProperty("cases")[0];
        var key = KeyDerivation.DerivePassphraseKey(
            Passphrase.ToKeyBytes(passphrase.GetProperty("passphrase").GetString()!),
            bundle.Salt, bundle.MemoryBytes, bundle.Passes);

        Assert.Equal(UserSecret, bundle.UnlockWithPassphraseKey(key, Id(Keys, "userId")));
    }

    [Fact]
    public void The_Recovery_Code_Opens_The_Secret_Key()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());

        Assert.Equal(UserSecret, bundle.UnlockWithRecoveryCode(Hex(Keys, "recoveryCodeHex"), Id(Keys, "userId")));
    }

    [Fact]
    public void The_Bundle_Is_Bound_To_Its_User()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());

        // the user id is part of the associated data: a bundle cannot be handed to another user
        Assert.Throws<OloopCryptoException>(
            () => bundle.UnlockWithRecoveryCode(Hex(Keys, "recoveryCodeHex"), Guid.NewGuid()));
    }

    [Fact]
    public void A_Wrong_Key_Does_Not_Open_The_Bundle()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText());

        Assert.Throws<OloopCryptoException>(
            () => bundle.UnlockWithPassphraseKey(new byte[32], Id(Keys, "userId")));
    }

    [Fact]
    public void A_Bundle_Whose_Public_Key_Is_Not_The_One_Of_The_Secret_Key_Is_Refused()
    {
        var bundle = KeyBundle.FromJson(Keys.GetProperty("bundle").GetRawText())
            with { PublicKey = PublicKeyBox.GenerateKeyPair().PublicKey };

        Assert.Throws<OloopCryptoException>(
            () => bundle.UnlockWithRecoveryCode(Hex(Keys, "recoveryCodeHex"), Id(Keys, "userId")));
    }

    [Theory]
    [InlineData("v", "2", typeof(UnknownEnvelopeException))]
    [InlineData("kdf", "\"scrypt\"", typeof(OloopCryptoException))]
    [InlineData("m", "1000", typeof(OloopCryptoException))]
    [InlineData("t", "1", typeof(OloopCryptoException))]
    [InlineData("m", "4294967296", typeof(OloopCryptoException))]
    [InlineData("salt", "\"AAAA\"", typeof(OloopCryptoException))]
    [InlineData("publicKey", "\"not base64!\"", typeof(OloopCryptoException))]
    [InlineData("wrappedSecretKey", "\"AQE\"", typeof(OloopCryptoException))]
    public void A_Bundle_That_Is_Wrong_Is_Refused(string field, string json, Type expected)
    {
        var fields = Keys.GetProperty("bundle").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetRawText());
        fields[field] = json;
        var text = "{" + string.Join(",", fields.Select(f => $"\"{f.Key}\":{f.Value}")) + "}";

        var ex = Assert.ThrowsAny<OloopCryptoException>(() => KeyBundle.FromJson(text));
        Assert.Equal(expected, ex.GetType());
    }

    [Fact]
    public void A_Bundle_That_Lacks_A_Field_Or_Is_No_Json_Is_Refused()
    {
        Assert.Throws<OloopCryptoException>(() => KeyBundle.FromJson("not json"));
        Assert.Throws<OloopCryptoException>(() => KeyBundle.FromJson("{}"));
    }

    // ---------------- Handing a project key over ----------------

    [Fact]
    public void Every_Grant_Of_The_Vectors_Opens_To_The_Project_Key()
    {
        foreach (var g in Cases(Root.GetProperty("grants")))
        {
            var recipientSecret = SecretOf(Hex(g, "recipientPublicKeyHex"));

            var key = ProjectKeyBox.Unwrap(Hex(g, "wrappedHex"), recipientSecret,
                Hex(g, "senderPublicKeyHex"), Id(g, "projectId"), g.GetProperty("keyVersion").GetInt32());

            Assert.Equal(Hex(g, "projectKeyHex"), key);
        }
    }

    [Fact]
    public void Wrapping_With_The_Nonce_Of_The_Vector_Gives_The_Bytes_Of_The_Vector()
    {
        foreach (var g in Cases(Root.GetProperty("grants")))
        {
            var wrapped = Hex(g, "wrappedHex");

            var again = ProjectKeyBox.Wrap(Hex(g, "projectKeyHex"), Id(g, "projectId"),
                g.GetProperty("keyVersion").GetInt32(), Hex(g, "recipientPublicKeyHex"),
                SecretOf(Hex(g, "senderPublicKeyHex")), wrapped[..ProjectKeyBox.NonceBytes]);

            Assert.Equal(wrapped, again);
        }
    }

    [Fact]
    public void A_Wrapped_Key_Has_The_Size_The_Server_Expects()
    {
        foreach (var g in Cases(Root.GetProperty("grants")))
            Assert.Equal(ProjectKeyBox.WrappedBytes, Hex(g, "wrappedHex").Length);
    }

    // ---------------- Documents and blobs ----------------

    [Fact]
    public void Every_Document_Of_The_Vectors_Opens_And_Is_Made_Again_Byte_For_Byte()
    {
        foreach (var d in Cases(Root.GetProperty("documents")))
        {
            var associated = AssociatedData.Document(
                d.GetProperty("kind").GetString()!, Id(d, "id"),
                d.GetProperty("keyVersion").GetInt32(), d.GetProperty("rev").GetInt32());
            var envelope = Hex(d, "envelopeHex");
            var key = Hex(d, "keyHex");
            var text = d.GetProperty("plaintextUtf8").GetString()!;

            Assert.Equal(Hex(d, "aadHex"), associated);
            Assert.Equal(text, Encoding.UTF8.GetString(Envelope.Open(key, envelope, associated)));
            Assert.Equal(envelope, Envelope.Seal(key, Encoding.UTF8.GetBytes(text), associated, Hex(d, "nonceHex")));
        }
    }

    [Fact]
    public void The_Blob_Of_The_Vectors_Opens_And_Is_Made_Again_Byte_For_Byte()
    {
        var b = Root.GetProperty("blob");
        var associated = AssociatedData.Blob(Id(b, "blobId"), Id(b, "taskId"), b.GetProperty("keyVersion").GetInt32());
        var envelope = Hex(b, "envelopeHex");

        Assert.Equal(Hex(b, "aadHex"), associated);
        Assert.Equal(Hex(b, "plaintextHex"), Envelope.Open(Hex(b, "keyHex"), envelope, associated));
        Assert.Equal(envelope, Envelope.Seal(Hex(b, "keyHex"), Hex(b, "plaintextHex"), associated, Hex(b, "nonceHex")));
        Assert.Equal(b.GetProperty("sha256Hex").GetString(), Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant());
    }

    // ---------------- Agents ----------------

    [Fact]
    public void The_Registration_Of_The_Agent_Opens()
    {
        var (publicKey, agentId) = AgentCrypto.OpenRegistration(
            Hex(Agents.GetProperty("registration"), "hex"), UserSecret, UserPublic);

        Assert.Equal(Hex(Agents.GetProperty("key1"), "publicKeyHex"), publicKey);
        Assert.Equal(Id(Agents, "agentId"), agentId);
    }

    [Fact]
    public void The_Statement_Of_The_Key_Rotation_Opens_And_Leads_To_The_New_Key()
    {
        var statement = Agents.GetProperty("statement");

        var (newKey, agentId, number) = AgentCrypto.OpenStatement(
            Hex(statement, "hex"), UserSecret, Hex(Agents.GetProperty("key1"), "publicKeyHex"));

        Assert.Equal(Hex(Agents.GetProperty("key2"), "publicKeyHex"), newKey);
        Assert.Equal(Id(Agents, "agentId"), agentId);
        Assert.Equal(statement.GetProperty("keyNumber").GetInt32(), number);
        Assert.Equal(2, number);
    }

    [Fact]
    public void The_Key_That_The_Agent_Handed_To_Itself_Opens()
    {
        var self = Agents.GetProperty("selfGrant");
        var key2 = Agents.GetProperty("key2");

        var key = ProjectKeyBox.Unwrap(Hex(self, "wrappedHex"), Hex(key2, "secretKeyHex"),
            Hex(key2, "publicKeyHex"), Id(self, "projectId"), self.GetProperty("keyVersion").GetInt32());

        Assert.Equal(Hex(self, "projectKeyHex"), key);
    }

    // ---------------- What has to be refused ----------------

    public static IEnumerable<object[]> MustFail()
        => Root.GetProperty("mustFail").EnumerateArray().Select((e, i) => new object[] { i, e.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(MustFail))]
    public void This_Input_Is_Refused(int index, string name)
    {
        _ = name; // only for the name of the test
        var c = Root.GetProperty("mustFail")[index];

        switch (c.GetProperty("kind").GetString())
        {
            case "envelope":
                Assert.Throws<OloopCryptoException>(
                    () => Envelope.Open(Hex(c, "keyHex"), Hex(c, "envelopeHex"), Hex(c, "aadHex")));
                break;

            case "envelope-header":
                Assert.Throws<UnknownEnvelopeException>(
                    () => Envelope.Open(Hex(c, "keyHex"), Hex(c, "envelopeHex"), []));
                break;

            case "grant":
                Assert.Throws<OloopCryptoException>(() => ProjectKeyBox.Unwrap(
                    Hex(c, "wrappedHex"), Hex(c, "recipientSecretKeyHex"), Hex(c, "pinnedSenderPublicKeyHex"),
                    Id(c, "projectId"), c.GetProperty("keyVersion").GetInt32()));
                break;

            case "statement":
                Assert.Throws<OloopCryptoException>(() => AgentCrypto.OpenStatement(
                    Hex(c, "statementHex"), Hex(c, "userSecretKeyHex"), Hex(c, "previousAgentPublicKeyHex")));
                break;

            default:
                throw new InvalidOperationException("a kind of input this test does not know: " + c.GetProperty("kind"));
        }
    }

    [Fact]
    public void Every_Kind_Of_Input_Of_The_Vectors_Is_Covered()
    {
        var kinds = Root.GetProperty("mustFail").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).Distinct();

        Assert.Equal(["envelope", "envelope-header", "grant", "statement"], kinds.Order());
    }
}
