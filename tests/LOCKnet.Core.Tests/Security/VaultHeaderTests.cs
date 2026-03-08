using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Tests.Security;

public class VaultHeaderTests
{
	[Fact]
	public void SerializeDeserialize_RoundTripsVaultHeader()
	{
		var header = new VaultHeader
		{
			FormatVersion = 1,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = [0x01, 0x02, 0x03],
			WrappedVaultKey = [0x0A, 0x0B, 0x0C],
			LegacyPasswordHash = [0xAA, 0xBB],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};

		var json = header.Serialize();
		var roundTripped = VaultHeader.Deserialize(json);

		Assert.Equal(header.FormatVersion, roundTripped.FormatVersion);
		Assert.Equal(header.KdfIdentifier, roundTripped.KdfIdentifier);
		Assert.Equal(header.KdfParameters.Iterations, roundTripped.KdfParameters.Iterations);
		Assert.Equal(header.Salt, roundTripped.Salt);
		Assert.Equal(header.WrappedVaultKey, roundTripped.WrappedVaultKey);
		Assert.Equal(header.LegacyPasswordHash, roundTripped.LegacyPasswordHash);
	}

	[Fact]
	public void SerializeDeserialize_RoundTripsKdfParameters()
	{
		var parameters = new VaultKdfParameters
		{
			HashAlgorithm = "SHA256",
			Iterations = 600_000,
			KeyLengthBytes = 32,
			SaltLengthBytes = 32,
		};

		var json = parameters.Serialize();
		var roundTripped = VaultKdfParameters.Deserialize(json);

		Assert.Equal(parameters.HashAlgorithm, roundTripped.HashAlgorithm);
		Assert.Equal(parameters.Iterations, roundTripped.Iterations);
		Assert.Equal(parameters.KeyLengthBytes, roundTripped.KeyLengthBytes);
		Assert.Equal(parameters.SaltLengthBytes, roundTripped.SaltLengthBytes);
	}
}
