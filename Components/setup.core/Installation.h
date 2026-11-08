#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <filesystem>
#include <string>
#include <vector>
#include <map>
#include <functional>
#include <stdexcept>
#include <atomic>
#include <memory>

namespace setup {
namespace fs = std::filesystem;
inline constexpr auto Product = L"EricGameLauncher";
inline constexpr auto UninstallKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\EricSoft.EricGameLauncher";
inline constexpr auto AppPathKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\EricGameLauncher.exe";
inline constexpr auto Manifest = L"._install_manifest";

struct Handle {
    HANDLE value = INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE handle = INVALID_HANDLE_VALUE) : value(handle) {}
    ~Handle() { if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct Key {
    HKEY value = nullptr;
    ~Key() { if (value) RegCloseKey(value); }
    Key() = default;
    Key(const Key&) = delete;
    Key& operator=(const Key&) = delete;
};
struct DirectoryLease {
    Handle installation, update;
    explicit DirectoryLease(const fs::path& root);
};
struct Record {
    std::wstring root, id, owner, desktop, menu, version, date, startup;
    DWORD size = 0;
    explicit operator bool() const { return !id.empty(); }
};
struct ShortcutLocations { std::wstring desktop, menu; };
struct Options {
    std::wstring root, owner;
    bool quiet = false, update = false, stage = false, worker = false, help = false;
    bool desktop = false, menu = true, extract = false;
    bool keepData = false, dataChoice = false;
    std::shared_ptr<std::atomic_bool> cancellation;
    std::wstring cancelFile;
    std::shared_ptr<const ShortcutLocations> shortcuts;
    std::shared_ptr<const fs::path> dataDirectory;
    std::function<void(const std::string&)> checkpoint;
    DWORD parent = 0;
};
std::wstring Wide(const std::string& value);
std::string Utf8(const std::wstring& value);
std::wstring Text(const std::string& key);
std::wstring Self();
std::wstring Sid();
std::wstring Guid();
std::wstring Folder(REFKNOWNFOLDERID id, DWORD flags = 0);
std::wstring ValidateRoot(const std::wstring& value);
std::wstring Destination(const std::wstring& value);
fs::path Owned(const fs::path& root, const std::wstring& relative);
void Check(bool result, const char* operation);
void Log(const std::wstring& message) noexcept;
std::wstring Quote(const std::wstring& value);
Options Parse();
Record ReadRegistration(const std::wstring& owner);
Record Find(const std::wstring& root, const std::wstring& owner);
Record RegisteredInstallation(const std::wstring& owner);
Record SelectInstallation(Options& options);
ShortcutLocations UserShortcuts(const Options& options);
std::wstring UpgradeSummary(const Record& installed, const std::wstring& target);
void WriteRegistration(const Record& record);
void RestoreRegistration(const Record& record);
void RemoveRegistration(const Record& record);
void Shortcut(const std::wstring& executable, const std::wstring& destination);
bool ShortcutTargets(const std::wstring& destination, const std::wstring& executable);
std::vector<std::wstring> ReadLines(const fs::path& path);
void WriteLines(const fs::path& path, const std::vector<std::wstring>& lines);
std::vector<unsigned char> Resource(int id);
bool CanWrite(const fs::path& root);
DWORD Run(const std::wstring& executable, const std::wstring& arguments, bool elevate, bool wait = true);
void CloseApplication(const std::wstring& root);
void Finalize(const Options& options, const std::wstring& version);
void Install(const Options& options, const std::wstring& package, const std::vector<unsigned char>& uninstaller, const std::wstring& version);
void ExtractOnly(const Options& options, const std::wstring& package);
void CheckCancelled(const Options& options);
bool PrepareUninstallWorker(const Options& options);
fs::path UninstallDataDirectory(const Options& options);
void Uninstall(const Options& options);
int Window(Options options, const std::function<void(const Options&)>& execute, bool uninstall);
}
