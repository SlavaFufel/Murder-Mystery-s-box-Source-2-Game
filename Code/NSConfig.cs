namespace Sandbox;

/// <summary>
/// Network Storage project configuration — hand-written equivalent of the
/// auto-generated file from Editor → Network Storage → Sync Tool.
///
/// Values mirror Assets/network-storage.credentials.json so the library can
/// configure itself on ANY client (host, non-host, published standalone build)
/// without relying on the JSON file being packed into the build — s&box doesn't
/// auto-include arbitrary JSON in Assets/. Compiled constants ship with the dll.
///
/// The public key (`sbox_ns_` prefix) is safe to embed — it identifies the
/// project, it's NOT a private/auth secret. Library uses TypeLibrary.GetType
/// to find this class at runtime; namespace doesn't matter.
/// </summary>
public static class NSConfig
{
	public const string ProjectId  = "d2a297e1ed214fec";
	public const string PublicKey  = "sbox_ns_4ffbbe482af7474898aa80468a3b606d";
	public const string BaseUrl    = "https://api.sboxcool.com";
	public const string ApiVersion = "v3";

	/// <summary>
	/// Хост проксирует Network Storage запросы для не-хостов. Включено для
	/// editor/local-testing (один Steam-ID на всех), на проде должно быть false —
	/// каждый игрок шлёт свои запросы со своим SteamID.
	/// </summary>
	public const bool ProxyEnabled = false;

	public const bool EnableAuthSessions = false;
	public const bool EnableEncryptedRequests = false;

	/// <summary>Принудительно сконфигурировать NetworkStorage. Делегирует EnsureConfigured.</summary>
	public static void EnsureConfigured() => NetworkStorage.EnsureConfigured();
}
