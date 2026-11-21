using System.Buffers.Binary;
using System.Security.Cryptography;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// What a client checks of the keys of an agent (AGENTS.md, "Creating an agent"
/// and "Rotating the key pair"): the registration that the user made at the
/// creation, and the statements that the agent makes for each new key pair. A
/// client follows them from the registration to the current key before it relies
/// on the public key that the server shows.
/// </summary>
public static class AgentCrypto
{
    private const int NonceBytes = 24;
    private const int PublicKeyBytes = 32;

    /// <summary>
    /// The registration: <c>nonce | box(agentPublicKey | agentId)</c>, from the user
    /// to the user. Only the key of the user can make it, so the server cannot
    /// invent a key of an agent.
    /// </summary>
    public static (byte[] AgentPublicKey, Guid AgentId) OpenRegistration(
        byte[] registration, byte[] userSecretKey, byte[] userPublicKey)
    {
        var message = Open(registration, userSecretKey, userPublicKey, "registration");
        if (message.Length != PublicKeyBytes + GuidBytes.Length)
            throw new OloopCryptoException("the registration of the agent has the wrong size");

        return (message[..PublicKeyBytes], GuidBytes.FromBytes(message.AsSpan(PublicKeyBytes)));
    }

    /// <summary>
    /// Makes the registration that <see cref="OpenRegistration"/> opens: <c>nonce |
    /// box(agentPublicKey | agentId)</c>, from the user to the user (88 bytes). The user makes it
    /// when the agent is created; the server stores it and cannot make one itself.
    /// </summary>
    public static byte[] CreateRegistration(
        byte[] agentPublicKey, Guid agentId, byte[] userSecretKey, byte[] userPublicKey)
        => CreateRegistration(agentPublicKey, agentId, userSecretKey, userPublicKey,
            SodiumCore.GetRandomBytes(NonceBytes));

    /// <summary>For the tests, which need the same bytes as the test vectors.</summary>
    internal static byte[] CreateRegistration(
        byte[] agentPublicKey, Guid agentId, byte[] userSecretKey, byte[] userPublicKey, byte[] nonce)
    {
        if (agentPublicKey.Length != PublicKeyBytes)
            throw new ArgumentException($"the public key of the agent must be {PublicKeyBytes} bytes", nameof(agentPublicKey));

        var message = new byte[PublicKeyBytes + GuidBytes.Length];
        agentPublicKey.CopyTo(message, 0);
        GuidBytes.ToBytes(agentId).CopyTo(message, PublicKeyBytes);

        var box = PublicKeyBox.Create(message, nonce, userSecretKey, userPublicKey);
        var registration = new byte[NonceBytes + box.Length];
        nonce.CopyTo(registration, 0);
        box.CopyTo(registration, NonceBytes);
        return registration;
    }

    /// <summary>
    /// The statement of a key rotation: <c>nonce | box(newPublicKey | agentId |
    /// keyNumber(4, big endian))</c>, made with the old secret key of the agent for
    /// the user. <c>keyNumber</c> is the number of the new key, the first rotation
    /// makes 2.
    /// </summary>
    public static (byte[] NewPublicKey, Guid AgentId, int KeyNumber) OpenStatement(
        byte[] statement, byte[] userSecretKey, byte[] previousAgentPublicKey)
    {
        var message = Open(statement, userSecretKey, previousAgentPublicKey, "statement");
        if (message.Length != PublicKeyBytes + GuidBytes.Length + 4)
            throw new OloopCryptoException("the statement of the agent has the wrong size");

        return (message[..PublicKeyBytes],
            GuidBytes.FromBytes(message.AsSpan(PublicKeyBytes, GuidBytes.Length)),
            (int)BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(PublicKeyBytes + GuidBytes.Length)));
    }

    private static byte[] Open(byte[] data, byte[] recipientSecretKey, byte[] senderPublicKey, string what)
    {
        if (data.Length <= NonceBytes)
            throw new OloopCryptoException($"the {what} of the agent is too short");

        try
        {
            return PublicKeyBox.Open(data[NonceBytes..], data[..NonceBytes], recipientSecretKey, senderPublicKey);
        }
        catch (CryptographicException ex)
        {
            throw new OloopCryptoException($"the {what} of the agent was not made by the key it is said to come from", ex);
        }
    }
}
