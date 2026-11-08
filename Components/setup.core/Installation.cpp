#include "Installation.h"
#include <shlobj.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include <sddl.h>
#include <tlhelp32.h>
#include <fstream>
#include <sstream>
#include <set>
#include <algorithm>
#include <memory>

namespace setup {
std::wstring Wide(const std::string& value) {
    if (value.empty()) return {};
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    Check(length > 0, "UTF8");
    std::wstring result(length, 0);
    Check(MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length) != 0, "UTF8");
    return result;
}
std::string Utf8(const std::wstring& value) {
    if (value.empty()) return {};
    int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    Check(length > 0, "UTF16");
    std::string result(length, 0);
    Check(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr) != 0, "UTF16");
    return result;
}
void Check(bool result, const char* operation) {
    if (!result) throw std::runtime_error(std::string(operation) + " (" + std::to_string(GetLastError()) + ")");
}
DirectoryLease::DirectoryLease(const fs::path& root) {
    installation.value = CreateFileW((root / L"._install_lock").c_str(), GENERIC_READ | GENERIC_WRITE | DELETE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
    Check(installation.value != INVALID_HANDLE_VALUE, "InstallationInProgress");
    update.value = CreateFileW((root / L"._update_lock").c_str(), GENERIC_READ | GENERIC_WRITE | DELETE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
    Check(update.value != INVALID_HANDLE_VALUE, "UpdateInProgress");
}
std::vector<unsigned char> Resource(int id) {
    HRSRC entry = FindResourceW(nullptr, MAKEINTRESOURCEW(id), RT_RCDATA);
    Check(entry != nullptr, "FindResource");
    DWORD size = SizeofResource(nullptr, entry);
    auto data = static_cast<const unsigned char*>(LockResource(LoadResource(nullptr, entry)));
    Check(data != nullptr && size > 0, "LoadResource");
    return { data, data + size };
}
std::wstring Text(const std::string& key) {
    static const auto strings = [] {
        auto bytes = Resource(101);
        std::istringstream stream(std::string(bytes.begin(), bytes.end()));
        std::map<std::string, std::wstring> result;
        bool active = false;
        std::string language = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_CHINESE ? "\"Zh-CN\":" : "\"EN\":";
        for (std::string line; std::getline(stream, line);) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (!line.empty() && line[0] != ' ') active = line == language;
            if (!active || line.rfind("  Installer_", 0) != 0) continue;
            auto colon = line.find(':');
            auto first = line.find('"', colon + 1), last = line.rfind('"');
            if (first == std::string::npos || first == last) throw std::runtime_error("TEXT_RESOURCE_INVALID");
            std::string value;
            for (auto i = first + 1; i < last; ++i) {
                if (line[i] == '\\') {
                    if (++i >= last) throw std::runtime_error("TEXT_RESOURCE_ESCAPE");
                    value += line[i] == 'n' ? '\n' : line[i] == 'r' ? '\r' : line[i] == 't' ? '\t' : line[i];
                } else value += line[i];
            }
            result.emplace(line.substr(2, colon - 2), Wide(value));
        }
        return result;
    }();
    auto found = strings.find(key);
    return found == strings.end() ? Wide(key) : found->second;
}
std::wstring Self() {
    std::wstring path(32768, 0);
    DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    Check(length > 0 && length < path.size(), "GetModuleFileName");
    path.resize(length);
    return path;
}
std::wstring Sid() {
    Handle token;
    Check(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value) != 0, "OpenProcessToken");
    DWORD length = 0;
    GetTokenInformation(token.value, TokenUser, nullptr, 0, &length);
    std::vector<unsigned char> data(length);
    Check(GetTokenInformation(token.value, TokenUser, data.data(), length, &length) != 0, "GetTokenInformation");
    LPWSTR sid = nullptr;
    Check(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid, &sid) != 0, "ConvertSidToStringSid");
    std::wstring result(sid);
    LocalFree(sid);
    return result;
}
std::wstring Guid() {
    GUID guid{};
    Check(SUCCEEDED(CoCreateGuid(&guid)), "CoCreateGuid");
    wchar_t buffer[40]{};
    Check(StringFromGUID2(guid, buffer, 40) > 0, "StringFromGUID2");
    std::wstring result;
    for (wchar_t c : std::wstring(buffer)) if (c != L'{' && c != L'}' && c != L'-') result += c;
    return result;
}
std::wstring Folder(REFKNOWNFOLDERID id, DWORD flags) {
    PWSTR path = nullptr;
    Check(SUCCEEDED(SHGetKnownFolderPath(id, flags, nullptr, &path)), "SHGetKnownFolderPath");
    std::wstring result(path);
    CoTaskMemFree(path);
    return result;
}
static bool Same(const fs::path& first, const fs::path& second) { return _wcsicmp(first.c_str(), second.c_str()) == 0; }
fs::path Owned(const fs::path& root, const std::wstring& relative) {
    if (relative.empty() || relative.find_first_of(L":\r\n\t") != std::wstring::npos) throw std::runtime_error("INVALID_OWNED_PATH");
    fs::path path(relative);
    if (path.is_absolute() || path.has_root_path()) throw std::runtime_error("INVALID_OWNED_PATH");
    for (const auto& part : path) {
        std::wstring name = part.wstring();
        if (name == L".." || name == L"." || name.empty() || name.back() == L'.' || name.back() == L' ' || name.find_first_of(L"<>\"|?*") != std::wstring::npos) throw std::runtime_error("INVALID_OWNED_PATH");
    }
    std::wstring top = path.begin()->wstring();
    if (_wcsicmp(top.c_str(), L"Data") == 0 || _wcsicmp(top.c_str(), L"Cache") == 0 || _wcsicmp(top.c_str(), L"storage.mode") == 0) throw std::runtime_error("USER_DATA_PATH");
    fs::path full = (root / path).lexically_normal();
    for (auto current = full; current.wstring().size() >= root.wstring().size(); current = current.parent_path()) {
        DWORD attributes = GetFileAttributesW(current.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT)) throw std::runtime_error("REPARSE_POINT");
        if (current == root || current == current.parent_path()) break;
    }
    return full;
}
static fs::path ExpandedPath(const std::wstring& value) {
    std::wstring expanded(32768, 0);
    DWORD length = ExpandEnvironmentStringsW(value.c_str(), expanded.data(), static_cast<DWORD>(expanded.size()));
    Check(length > 1 && length <= expanded.size(), "ExpandEnvironmentStrings");
    expanded.resize(length - 1);
    fs::path root = fs::absolute(expanded).lexically_normal();
    while (root != root.root_path() && root.filename().empty()) root = root.parent_path();
    return root;
}
std::wstring Destination(const std::wstring& value) {
    auto root = ExpandedPath(value);
    if (!Same(root.filename(), Product)) root /= Product;
    return ValidateRoot(root.wstring());
}
std::wstring ValidateRoot(const std::wstring& value) {
    auto root = ExpandedPath(value);
    if (root == root.root_path()) throw std::runtime_error(Utf8(Text("Installer_InvalidDirectory")));
    for (const auto& id : { FOLDERID_Profile, FOLDERID_LocalAppData, FOLDERID_RoamingAppData, FOLDERID_ProgramData, FOLDERID_Desktop, FOLDERID_Programs, FOLDERID_ProgramFiles, FOLDERID_ProgramFilesX86 })
        if (Same(root, Folder(id))) throw std::runtime_error(Utf8(Text("Installer_InvalidDirectory")));
    auto windows = Folder(FOLDERID_Windows);
    if (Same(root, windows) || _wcsnicmp(root.c_str(), (windows + L"\\").c_str(), windows.size() + 1) == 0) throw std::runtime_error(Utf8(Text("Installer_InvalidDirectory")));
    Owned(root, L"EricGameLauncher.exe");
    return root.wstring();
}
std::vector<std::wstring> ReadLines(const fs::path& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("READ_MANIFEST_FAILED");
    std::vector<std::wstring> result;
    for (std::string line; std::getline(input, line);) { if (!line.empty() && line.back() == '\r') line.pop_back(); if (!line.empty()) result.push_back(Wide(line)); }
    if (input.bad()) throw std::runtime_error("READ_MANIFEST_FAILED");
    return result;
}
void WriteLines(const fs::path& path, const std::vector<std::wstring>& lines) {
    Handle output(CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    Check(output.value != INVALID_HANDLE_VALUE, "CreateFile");
    for (const auto& line : lines) {
        auto bytes = Utf8(line) + "\n";
        DWORD written = 0;
        Check(WriteFile(output.value, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) != 0 && written == bytes.size(), "WriteFile");
    }
    Check(FlushFileBuffers(output.value) != 0, "FlushFileBuffers");
}
void Log(const std::wstring& message) noexcept {
    try {
        wchar_t temporary[32768]{};
        Check(GetTempPathW(32768, temporary) > 0, "GetTempPath");
        fs::path folder = fs::path(temporary) / L"eric" / L"ericgamelauncher" / L"setup";
        fs::create_directories(folder);
        std::ofstream output(folder / (std::to_wstring(GetCurrentProcessId()) + L".log"), std::ios::app | std::ios::binary);
        SYSTEMTIME time{};
        GetSystemTime(&time);
        output << time.wYear << '-' << time.wMonth << '-' << time.wDay << ' ' << time.wHour << ':' << time.wMinute << ':' << time.wSecond << "Z " << Utf8(message) << '\n';
    } catch (...) {}
}
std::wstring Quote(const std::wstring& value) {
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (wchar_t c : value) {
        if (c == L'\\') { ++slashes; continue; }
        result.append(slashes * (c == L'"' ? 2 : 1), L'\\');
        slashes = 0;
        if (c == L'"') result += L'\\';
        result += c;
    }
    result.append(slashes * 2, L'\\');
    return result + L'"';
}
fs::path UninstallDataDirectory(const Options& options) {
    auto path = options.dataDirectory ? *options.dataDirectory : fs::path(Folder(FOLDERID_ProgramData)) / L"eric" / Product;
    if (path.empty() || !path.is_absolute()) throw std::runtime_error("UNINSTALL_INVALID_DATA_DIRECTORY");
    path = path.lexically_normal().make_preferred();
    while (path != path.root_path() && path.filename().empty()) path = path.parent_path();
    return path;
}
Options Parse() {
    int count = 0;
    LPWSTR* values = CommandLineToArgvW(GetCommandLineW(), &count);
    Check(values != nullptr, "CommandLineToArgv");
    std::unique_ptr<LPWSTR, decltype(&LocalFree)> arguments(values, LocalFree);
    Options result;
    result.owner = Sid();
    std::set<std::wstring> seen;
    for (int i = 1; i < count; ++i) {
        std::wstring key(values[i]);
        if (!seen.insert(key).second) throw std::runtime_error(Utf8(Text("Installer_InvalidArguments")));
        if (key == L"--directory" || key == L"--owner-sid" || key == L"--parent" || key == L"--cancel-file") {
            if (++i >= count) throw std::runtime_error(Utf8(Text("Installer_InvalidArguments")));
            if (key == L"--directory") result.root = values[i];
            else if (key == L"--owner-sid") result.owner = values[i];
            else if (key == L"--cancel-file") result.cancelFile = values[i];
            else result.parent = std::stoul(values[i]);
        } else if (key == L"--quiet") result.quiet = true;
        else if (key == L"--keep-data" || key == L"--remove-data") {
            if (result.dataChoice) throw std::runtime_error(Utf8(Text("Installer_InvalidArguments")));
            result.keepData = key == L"--keep-data";
            result.dataChoice = true;
        }
        else if (key == L"--update") result.update = true;
        else if (key == L"--stage") result.stage = true;
        else if (key == L"--worker") result.worker = true;
        else if (key == L"--no-desktop") result.desktop = false;
        else if (key == L"--desktop") result.desktop = true;
        else if (key == L"--no-start-menu") result.menu = false;
        else if (key == L"--extract") result.extract = true;
        else if (key == L"--help") result.help = true;
        else throw std::runtime_error(Utf8(Text("Installer_InvalidArguments")));
    }
    PSID parsed = nullptr;
    Check(ConvertStringSidToSidW(result.owner.c_str(), &parsed) != 0, "OwnerSid");
    LocalFree(parsed);
    return result;
}
void CheckCancelled(const Options& options) {
    if ((options.cancellation && options.cancellation->load()) || (!options.cancelFile.empty() && fs::exists(options.cancelFile))) throw std::runtime_error("INSTALL_CANCELLED");
}
static std::wstring UserPath(const std::wstring& owner, const wchar_t* path, HKEY& hive) {
    if (owner == Sid()) { hive = HKEY_CURRENT_USER; return path; }
    hive = HKEY_USERS;
    return owner + L"\\" + path;
}
static std::wstring Get(HKEY key, const wchar_t* name) {
    DWORD type = 0, bytes = 0;
    if (RegQueryValueExW(key, name, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS || type != REG_SZ || bytes < 2 || bytes > 65536) return {};
    std::wstring value(bytes / 2, 0);
    if (RegQueryValueExW(key, name, nullptr, &type, reinterpret_cast<BYTE*>(value.data()), &bytes) != ERROR_SUCCESS) return {};
    while (!value.empty() && value.back() == 0) value.pop_back();
    return value;
}
Record ReadRegistration(const std::wstring& owner) {
    HKEY hive;
    auto path = UserPath(owner, UninstallKey, hive);
    Key key;
    LSTATUS opened = RegOpenKeyExW(hive, path.c_str(), 0, KEY_READ, &key.value);
    if (opened == ERROR_FILE_NOT_FOUND) return {};
    Check(opened == ERROR_SUCCESS, "RegOpenKeyEx");
    Record record;
    record.owner = owner;
    record.root = Get(key.value, L"InstallLocation");
    record.id = Get(key.value, L"InstallId");
    if (record.id.size() != 32 || record.id.find_first_not_of(L"0123456789abcdefABCDEF") != std::wstring::npos || Get(key.value, L"OwnerSid") != owner || record.root.empty()) return {};
    record.desktop = Get(key.value, L"DesktopShortcut");
    record.menu = Get(key.value, L"StartMenuShortcut");
    record.version = Get(key.value, L"DisplayVersion");
    record.date = Get(key.value, L"InstallDate");
    DWORD bytes = sizeof(record.size);
    RegGetValueW(key.value, nullptr, L"EstimatedSize", RRF_RT_REG_DWORD, nullptr, &record.size, &bytes);
    Key startup;
    auto startupPath = UserPath(owner, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", hive);
    if (RegOpenKeyExW(hive, startupPath.c_str(), 0, KEY_READ, &startup.value) == ERROR_SUCCESS) {
        auto command = Get(startup.value, Product);
        auto prefix = Quote((fs::path(record.root) / L"EricGameLauncher.exe").wstring());
        if (_wcsnicmp(command.c_str(), prefix.c_str(), prefix.size()) == 0) record.startup = command;
    }
    return record;
}
Record Find(const std::wstring& root, const std::wstring& owner) {
    auto record = ReadRegistration(owner);
    if (!record || !Same(fs::absolute(record.root).lexically_normal(), fs::absolute(root).lexically_normal()) || !fs::is_regular_file(fs::path(root) / L"Uninstall.exe") || !fs::is_regular_file(fs::path(root) / L"EricGameLauncher.exe")) return {};
    return record;
}
Record RegisteredInstallation(const std::wstring& owner) {
    auto record = ReadRegistration(owner);
    if (!record) return {};
    try { return Find(record.root, owner); }
    catch (const fs::filesystem_error&) { Log(L"Ignored invalid installation path in registry"); return {}; }
}
ShortcutLocations UserShortcuts(const Options& options) {
    if (options.shortcuts) return *options.shortcuts;
    if (options.owner == Sid()) return {
        (fs::path(Folder(FOLDERID_Desktop, KF_FLAG_DONT_VERIFY)) / L"EricGameLauncher.lnk").wstring(),
        (fs::path(Folder(FOLDERID_Programs, KF_FLAG_DONT_VERIFY)) / L"EricGameLauncher.lnk").wstring()
    };
    HKEY hive;
    auto path = UserPath(options.owner, L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Folders", hive);
    Key key;
    Check(RegOpenKeyExW(hive, path.c_str(), 0, KEY_READ, &key.value) == ERROR_SUCCESS, "OpenUserShellFolders");
    auto desktop = Get(key.value, L"Desktop"), menu = Get(key.value, L"Programs");
    if (!fs::path(desktop).is_absolute() || !fs::path(menu).is_absolute()) throw std::runtime_error("INSTALL_SHORTCUT_FOLDER_MISSING");
    return {(fs::path(desktop) / L"EricGameLauncher.lnk").wstring(), (fs::path(menu) / L"EricGameLauncher.lnk").wstring()};
}
Record SelectInstallation(Options& options) {
    auto installed = RegisteredInstallation(options.owner);
    options.extract = false;
    options.update = static_cast<bool>(installed);
    if (installed) {
        options.root = installed.root;
        options.desktop = fs::is_regular_file(UserShortcuts(options).desktop);
        options.menu = true;
    }
    return installed;
}
std::wstring UpgradeSummary(const Record& installed, const std::wstring& target) {
    auto version = installed.version.empty() ? Text("Installer_UnknownVersion") : installed.version;
    return version + L" → " + target;
}
static void Set(HKEY key, const wchar_t* name, const std::wstring& value) {
    Check(RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS, "RegSetValueEx");
}
static void Number(HKEY key, const wchar_t* name, DWORD value) {
    Check(RegSetValueExW(key, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value)) == ERROR_SUCCESS, "RegSetValueEx");
}
void WriteRegistration(const Record& record) {
    HKEY hive;
    auto path = UserPath(record.owner, UninstallKey, hive);
    Key key;
    Check(RegCreateKeyExW(hive, path.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key.value, nullptr) == ERROR_SUCCESS, "RegCreateKeyEx");
    auto executable = (fs::path(record.root) / L"EricGameLauncher.exe").wstring();
    auto uninstall = Quote((fs::path(record.root) / L"Uninstall.exe").wstring()) + L" --owner-sid " + Quote(record.owner);
    Set(key.value, L"DisplayName", Product);
    Set(key.value, L"DisplayVersion", record.version);
    Set(key.value, L"Publisher", L"EricSoft");
    Set(key.value, L"InstallLocation", record.root);
    Set(key.value, L"DisplayIcon", Quote(executable) + L",0");
    Set(key.value, L"UninstallString", uninstall);
    Set(key.value, L"QuietUninstallString", uninstall + L" --quiet --remove-data");
    Set(key.value, L"InstallDate", record.date);
    Number(key.value, L"EstimatedSize", record.size);
    Number(key.value, L"NoModify", 1);
    Number(key.value, L"NoRepair", 1);
    Set(key.value, L"InstallId", record.id);
    Set(key.value, L"OwnerSid", record.owner);
    Set(key.value, L"DesktopShortcut", record.desktop);
    Set(key.value, L"StartMenuShortcut", record.menu);
    Key app;
    path = UserPath(record.owner, AppPathKey, hive);
    Check(RegCreateKeyExW(hive, path.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &app.value, nullptr) == ERROR_SUCCESS, "RegCreateKeyEx");
    Set(app.value, nullptr, executable);
}
void RestoreRegistration(const Record& record) {
    WriteRegistration(record);
    if (!record.startup.empty()) {
        HKEY hive;
        Key startup;
        auto path = UserPath(record.owner, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", hive);
        Check(RegCreateKeyExW(hive, path.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &startup.value, nullptr) == ERROR_SUCCESS, "CreateStartupRegistration");
        Set(startup.value, Product, record.startup);
    }
}
void RemoveRegistration(const Record& record) {
    auto existing = ReadRegistration(record.owner);
    if (!existing || existing.id != record.id || !Same(existing.root, record.root)) throw std::runtime_error("INSTALL_ID_MISMATCH");
    HKEY hive;
    auto path = UserPath(record.owner, UninstallKey, hive);
    Check(RegDeleteTreeW(hive, path.c_str()) == ERROR_SUCCESS, "RegDeleteTree");
    path = UserPath(record.owner, AppPathKey, hive);
    Key app;
    if (RegOpenKeyExW(hive, path.c_str(), 0, KEY_READ, &app.value) == ERROR_SUCCESS && Same(Get(app.value, nullptr), fs::path(record.root) / L"EricGameLauncher.exe")) {
        RegCloseKey(app.value); app.value = nullptr;
        Check(RegDeleteTreeW(hive, path.c_str()) == ERROR_SUCCESS, "RegDeleteTree");
    }
    path = UserPath(record.owner, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", hive);
    Key startup;
    if (RegOpenKeyExW(hive, path.c_str(), 0, KEY_READ | KEY_WRITE, &startup.value) == ERROR_SUCCESS) {
        auto command = Get(startup.value, Product);
        auto prefix = Quote((fs::path(record.root) / L"EricGameLauncher.exe").wstring());
        if (_wcsnicmp(command.c_str(), prefix.c_str(), prefix.size()) == 0) RegDeleteValueW(startup.value, Product);
    }
}
void Shortcut(const std::wstring& executable, const std::wstring& destination) {
    IShellLinkW* raw = nullptr;
    Check(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&raw))), "CoCreateInstance");
    std::unique_ptr<IShellLinkW, void(*)(IShellLinkW*)> link(raw, [](auto p) { p->Release(); });
    Check(SUCCEEDED(link->SetPath(executable.c_str())), "SetPath");
    Check(SUCCEEDED(link->SetWorkingDirectory(fs::path(executable).parent_path().c_str())), "SetWorkingDirectory");
    Check(SUCCEEDED(link->SetDescription(Product)), "SetDescription");
    IPropertyStore* properties = nullptr;
    Check(SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&properties))), "IPropertyStore");
    std::unique_ptr<IPropertyStore, void(*)(IPropertyStore*)> store(properties, [](auto p) { p->Release(); });
    PROPVARIANT identifier{};
    Check(SUCCEEDED(InitPropVariantFromString(L"EricSoft.EricGameLauncher", &identifier)), "InitPropVariant");
    HRESULT result = store->SetValue(PKEY_AppUserModel_ID, identifier);
    PropVariantClear(&identifier);
    Check(SUCCEEDED(result) && SUCCEEDED(store->Commit()), "SetAppId");
    IPersistFile* persisted = nullptr;
    Check(SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&persisted))), "IPersistFile");
    std::unique_ptr<IPersistFile, void(*)(IPersistFile*)> file(persisted, [](auto p) { p->Release(); });
    fs::create_directories(fs::path(destination).parent_path());
    auto temporary = destination + L"." + Guid() + L".lnk";
    try {
        Check(SUCCEEDED(file->Save(temporary.c_str(), TRUE)), "SaveShortcut");
        Check(MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != 0, "MoveShortcut");
    } catch (...) { DeleteFileW(temporary.c_str()); throw; }
}
bool ShortcutTargets(const std::wstring& destination, const std::wstring& executable) {
    try {
        IShellLinkW* raw = nullptr;
        Check(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&raw))), "CoCreateInstance");
        std::unique_ptr<IShellLinkW, void(*)(IShellLinkW*)> link(raw, [](auto p) { p->Release(); });
        IPersistFile* persisted = nullptr;
        Check(SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&persisted))), "IPersistFile");
        std::unique_ptr<IPersistFile, void(*)(IPersistFile*)> file(persisted, [](auto p) { p->Release(); });
        Check(SUCCEEDED(file->Load(destination.c_str(), STGM_READ)), "LoadShortcut");
        wchar_t target[32768]{};
        Check(SUCCEEDED(link->GetPath(target, 32768, nullptr, SLGP_RAWPATH)), "GetShortcutPath");
        return Same(target, executable);
    } catch (...) { return false; }
}
bool CanWrite(const fs::path& root) {
    auto existing = root;
    while (!fs::exists(existing) && existing != existing.parent_path()) existing = existing.parent_path();
    auto path = existing / (L".egl-setup-" + Guid());
    Handle probe(CreateFileW(path.c_str(), GENERIC_WRITE | DELETE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
    return probe.value != INVALID_HANDLE_VALUE;
}
DWORD Run(const std::wstring& executable, const std::wstring& arguments, bool elevate, bool wait) {
    HANDLE process = nullptr;
    if (elevate) {
        SHELLEXECUTEINFOW start{ sizeof(start) };
        start.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
        start.lpVerb = L"runas";
        start.lpFile = executable.c_str();
        start.lpParameters = arguments.c_str();
        start.nShow = SW_HIDE;
        Check(ShellExecuteExW(&start) != 0, "ShellExecuteEx");
        process = start.hProcess;
    } else {
        std::wstring command = Quote(executable) + L" " + arguments;
        STARTUPINFOW startup{ sizeof(startup) };
        PROCESS_INFORMATION information{};
        Check(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, fs::path(executable).parent_path().c_str(), &startup, &information) != 0, "CreateProcess");
        CloseHandle(information.hThread);
        process = information.hProcess;
    }
    Handle child(process);
    Check(process != nullptr, "ChildProcess");
    if (!wait) return 0;
    Check(WaitForSingleObject(process, INFINITE) == WAIT_OBJECT_0, "WaitForSingleObject");
    DWORD exitCode = 1;
    Check(GetExitCodeProcess(process, &exitCode) != 0, "GetExitCodeProcess");
    return exitCode;
}
void CloseApplication(const std::wstring& root) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    Check(snapshot.value != INVALID_HANDLE_VALUE, "ProcessSnapshot");
    PROCESSENTRY32W entry{ sizeof(entry) };
    for (BOOL more = Process32FirstW(snapshot.value, &entry); more; more = Process32NextW(snapshot.value, &entry)) {
        bool cli = _wcsicmp(entry.szExeFile, L"EricGameLauncher.Cli.exe") == 0;
        if (!cli && _wcsicmp(entry.szExeFile, L"EricGameLauncher.exe") != 0) continue;
        Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, entry.th32ProcessID));
        if (process.value == nullptr) continue;
        wchar_t executable[32768]{};
        DWORD size = 32768;
        if (!QueryFullProcessImageNameW(process.value, 0, executable, &size) || !Same(executable, fs::path(root) / (cli ? L"EricGameLauncher.Cli.exe" : L"EricGameLauncher.exe"))) continue;
        if (cli) {
            if (WaitForSingleObject(process.value, 15000) != WAIT_OBJECT_0) throw std::runtime_error(Utf8(Text("Installer_CloseApplication")));
            continue;
        }
        const auto pipePath = L"\\\\.\\pipe\\EricGameLauncher.ServiceHost.v1";
        WaitNamedPipeW(pipePath, 500);
        Handle pipe(CreateFileW(pipePath, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr));
        ULONG owner = 0;
        if (pipe.value != INVALID_HANDLE_VALUE && GetNamedPipeServerProcessId(pipe.value, &owner) && owner == entry.th32ProcessID) {
            DWORD written = 0;
            WriteFile(pipe.value, "EXIT", 4, &written, nullptr);
            FlushFileBuffers(pipe.value);
        }
        EnumWindows([](HWND window, LPARAM id) -> BOOL { DWORD pid = 0; GetWindowThreadProcessId(window, &pid); if (pid == static_cast<DWORD>(id)) PostMessageW(window, WM_CLOSE, 0, 0); return TRUE; }, static_cast<LPARAM>(entry.th32ProcessID));
        if (WaitForSingleObject(process.value, 10000) != WAIT_OBJECT_0) throw std::runtime_error(Utf8(Text("Installer_CloseApplication")));
    }
}
}
