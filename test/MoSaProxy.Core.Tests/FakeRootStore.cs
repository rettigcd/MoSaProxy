using System.Security.Cryptography.X509Certificates;

namespace MoSaProxy.Tests;

/// <summary>An in-memory stand-in for the Windows trusted-root store, so tests never touch the real one.</summary>
public sealed class FakeRootStore : IRootStore {
	readonly List<(string Thumbprint, string Subject, bool HadPrivateKey)> _installed = new();

	/// <summary>When set, Add throws this (like a user pressing No on the Windows confirmation dialog).</summary>
	public Exception? AddFailure { get; set; }

	/// <summary>When set, FindThumbprintsBySubject throws this (like an unreadable store).</summary>
	public Exception? ReadFailure { get; set; }

	public IReadOnlyList<string> Thumbprints => _installed.Select(i => i.Thumbprint).ToList();
	public bool AnyInstalledCopyHadAPrivateKey => _installed.Any(i => i.HadPrivateKey);
	public List<string> Log { get; } = new();

	public void Preinstall(string thumbprint, string subject) => _installed.Add((thumbprint, subject, false));

	public IReadOnlyList<string> FindThumbprintsBySubject(string subject) {
		if (ReadFailure is not null) throw ReadFailure;
		return _installed.Where(i => i.Subject == subject).Select(i => i.Thumbprint).ToList();
	}

	public void Add(X509Certificate2 certificate) {
		Log.Add("add " + certificate.Thumbprint);
		if (AddFailure is not null) throw AddFailure;
		_installed.Add((certificate.Thumbprint, certificate.Subject, certificate.HasPrivateKey));
	}

	public void Remove(string thumbprint) {
		Log.Add("remove " + thumbprint);
		_installed.RemoveAll(i => i.Thumbprint == thumbprint);
	}
}
