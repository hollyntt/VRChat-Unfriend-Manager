using System.Text.Json;
using VRCUFM.Filesystem;

namespace VRCUFM.AppSystem;

/// <summary>
/// A named application account and the isolated configuration file it uses.
/// </summary>
public sealed class AccountProfile
{
    internal AccountProfile(string id, string displayName, string configPath)
    {
        Id = id;
        DisplayName = displayName;
        ConfigPath = configPath;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string ConfigPath { get; }
    public bool IsDefault => string.Equals(Id, AccountManager.DefaultAccountId, StringComparison.Ordinal);
}

/// <summary>
/// Manages per-account AppConfig files while retaining user.config as the default account.
/// </summary>
public sealed class AccountManager
{
    public const string DefaultAccountId = "default";
    private const string ConfigFileName = "AppConfig.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _appDataFolder;
    private string _currentAccountId = DefaultAccountId;
    private readonly Dictionary<string, AppConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public AccountManager() : this(Paths.AppDataFolder) { }

    public AccountManager(string appDataFolder)
    {
        if (string.IsNullOrWhiteSpace(appDataFolder))
            throw new ArgumentException("An application data folder is required.", nameof(appDataFolder));

        _appDataFolder = Path.GetFullPath(appDataFolder);
    }

    public AccountProfile CurrentProfile => GetProfile(_currentAccountId);

    public IReadOnlyList<AccountProfile> Profiles => ListProfiles();

    public AppConfig LoadCurrentProfile() => LoadProfile(CurrentProfile);

    public void SaveCurrentProfile(AppConfig config) => SaveProfile(CurrentProfile, config);

    public IReadOnlyList<AccountProfile> ListProfiles()
    {
        var defaultProfile = GetProfile(DefaultAccountId);
        var defaultConfig = LoadProfile(defaultProfile);
        var profiles = new List<AccountProfile>
        {
            GetProfile(DefaultAccountId, string.IsNullOrWhiteSpace(defaultConfig.Username) ? "Default account" : defaultConfig.Username)
        };
        string accountsFolder = Path.Combine(_appDataFolder, "accounts");
        if (!Directory.Exists(accountsFolder)) return profiles;

        foreach (string directory in Directory.EnumerateDirectories(accountsFolder))
        {
            string id = Path.GetFileName(directory);
            if (!IsSafeAccountId(id) || string.Equals(id, DefaultAccountId, StringComparison.Ordinal)) continue;
            var profile = GetProfile(id);
            var profileConfig = LoadProfile(profile);
            profiles.Add(GetProfile(id, string.IsNullOrWhiteSpace(profileConfig.Username) ? id : profileConfig.Username));
        }

        return profiles.OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public AccountProfile CreateProfile(string accountId, string? displayName = null, AppConfig? initialConfig = null)
    {
        string safeId = NormalizeAccountId(accountId);
        if (string.Equals(safeId, DefaultAccountId, StringComparison.Ordinal))
            throw new InvalidOperationException("The default account already exists.");

        AccountProfile profile = GetProfile(safeId, displayName);
        if (File.Exists(profile.ConfigPath))
            throw new InvalidOperationException($"An account with id '{safeId}' already exists.");

        Directory.CreateDirectory(Path.GetDirectoryName(profile.ConfigPath)!);
        SaveProfile(profile, initialConfig ?? new AppConfig());
        return profile;
    }

    public AppConfig LoadProfile(string accountId) => LoadProfile(GetProfile(accountId));

    public AppConfig LoadProfile(AccountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!File.Exists(profile.ConfigPath)) return new AppConfig();

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(profile.ConfigPath)) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
        catch (IOException)
        {
            return new AppConfig();
        }
    }

    public void SaveProfile(string accountId, AppConfig config) => SaveProfile(GetProfile(accountId), config);

    public void SaveProfile(AccountProfile profile, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(Path.GetDirectoryName(profile.ConfigPath)!);
        File.WriteAllText(profile.ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public void SwitchProfile(string accountId)
    {
        string safeId = NormalizeAccountId(accountId);
        if (!ListProfiles().Any(profile => string.Equals(profile.Id, safeId, StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"Account profile '{safeId}' was not found.");
        _currentAccountId = safeId;
    }

    public AccountProfile FindByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return CurrentProfile;
        return ListProfiles()
            .FirstOrDefault(profile => string.Equals(LoadProfile(profile).Username, username.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? CurrentProfile;
    }

    public AccountProfile EnsureProfileForLogin(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return CurrentProfile;
        var normalized = username.Trim();
        var existing = ListProfiles().FirstOrDefault(profile =>
            string.Equals(LoadProfile(profile).Username, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = CreateProfile(normalized, normalized, new AppConfig { Username = normalized });
        }

        if (!string.Equals(existing.Id, CurrentProfile.Id, StringComparison.OrdinalIgnoreCase))
            SwitchProfile(existing.Id);
        return existing;
    }

    public AccountProfile FindByCookie(string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return CurrentProfile;
        string auth = cookie.Split(';', 2)[0].Trim();
        return ListProfiles().FirstOrDefault(profile =>
        {
            string saved = LoadProfile(profile).Cookie ?? "";
            return saved.Split(';', 2)[0].Trim().Equals(auth, StringComparison.Ordinal);
        }) ?? CurrentProfile;
    }

    public bool DeleteProfile(string accountId)
    {
        string safeId = NormalizeAccountId(accountId);
        if (string.Equals(safeId, DefaultAccountId, StringComparison.Ordinal))
            throw new InvalidOperationException("The default account cannot be deleted.");

        string directory = Path.Combine(_appDataFolder, "accounts", safeId);
        if (!Directory.Exists(directory)) return false;
        if (string.Equals(_currentAccountId, safeId, StringComparison.Ordinal))
            SwitchProfile(DefaultAccountId);
        Directory.Delete(directory, recursive: true);
        return true;
    }

    public string GetConfigPath(string accountId) => GetProfile(accountId).ConfigPath;

    private AccountProfile GetProfile(string accountId, string? displayName = null)
    {
        string safeId = NormalizeAccountId(accountId);
        string configPath = string.Equals(safeId, DefaultAccountId, StringComparison.Ordinal)
            ? Path.Combine(_appDataFolder, "user.config")
            : Path.Combine(_appDataFolder, "accounts", safeId, ConfigFileName);
        return new AccountProfile(safeId, string.IsNullOrWhiteSpace(displayName) ? safeId : displayName.Trim(), configPath);
    }

    private static string NormalizeAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("An account id is required.", nameof(accountId));
        string trimmed = accountId.Trim();
        var chars = trimmed.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        string safeId = new string(chars).Trim('.');
        if (!IsSafeAccountId(safeId)) throw new ArgumentException("The account id does not contain a safe file name.", nameof(accountId));
        return safeId;
    }

    private static bool IsSafeAccountId(string accountId) =>
        !string.IsNullOrWhiteSpace(accountId) && accountId is not "." and not ".." &&
        accountId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
