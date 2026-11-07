#include "Installation.h"
#include <bcrypt.h>
#include <set>
#include <algorithm>

namespace setup {
static std::vector<unsigned char> Hash(const fs::path& path) {
    Handle input(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    Check(input.value != INVALID_HANDLE_VALUE, "OpenUninstaller");
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0, "SHA256Provider");
    BCRYPT_HASH_HANDLE hash = nullptr;
    try {
        Check(BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0, "SHA256Create");
        unsigned char buffer[65536]{};
        DWORD read = 0;
        do {
            Check(ReadFile(input.value, buffer, sizeof(buffer), &read, nullptr) != 0, "ReadUninstaller");
            if (read) Check(BCryptHashData(hash, buffer, read, 0) == 0, "SHA256Data");
        } while (read);
        std::vector<unsigned char> result(32);
        Check(BCryptFinishHash(hash, result.data(), static_cast<ULONG>(result.size()), 0) == 0, "SHA256Finish");
        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return result;
    } catch (...) {
        if (hash) BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw;
    }
}
bool PrepareUninstallWorker(const Options& options) {
    auto record = Find(options.root, options.owner);
    if (!record) throw std::runtime_error("INSTALL_REGISTRATION_MISMATCH");
    auto installed = fs::path(record.root) / L"Uninstall.exe";
    bool atInstalledPath = _wcsicmp(Self().c_str(), installed.c_str()) == 0;
    if (options.worker) {
        if (atInstalledPath) throw std::runtime_error("UNINSTALL_WORKER_MUST_BE_TEMPORARY");
        if (options.parent) {
            Handle parent(OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, options.parent));
            if (parent.value != nullptr) {
                wchar_t path[32768]{};
                DWORD count = 32768;
                Check(QueryFullProcessImageNameW(parent.value, 0, path, &count) != 0 && _wcsicmp(path, installed.c_str()) == 0, "UninstallParentIdentity");
                Check(WaitForSingleObject(parent.value, 15000) == WAIT_OBJECT_0, "UninstallParentExit");
            }
        }
        if (Hash(installed) != Hash(Self())) throw std::runtime_error("UNINSTALLER_IDENTITY_MISMATCH");
        return true;
    }
    if (!atInstalledPath) throw std::runtime_error("UNINSTALLER_LOCATION_MISMATCH");
    wchar_t temporary[32768]{};
    Check(GetTempPathW(32768, temporary) > 0, "GetTempPath");
    auto folder = fs::path(temporary) / L"eric" / L"ericgamelauncher" / L"uninstall" / Guid();
    fs::create_directories(folder);
    auto worker = folder / L"Uninstall.exe";
    Check(CopyFileW(installed.c_str(), worker.c_str(), TRUE) != 0, "CopyUninstallWorker");
    auto parameters = L"--worker --directory " + Quote(record.root) + L" --owner-sid " + Quote(options.owner) + L" --parent " + std::to_wstring(GetCurrentProcessId()) + (options.quiet ? L" --quiet" : L"");
    Run(worker.wstring(), parameters, false, false);
    Log(L"Uninstall handed off to temporary worker");
    return false;
}
void Uninstall(const Options& options) {
    auto root = ValidateRoot(options.root);
    auto record = Find(root, options.owner);
    if (!record) throw std::runtime_error("INSTALL_REGISTRATION_MISMATCH");
    if (Hash(fs::path(root) / L"Uninstall.exe") != Hash(Self())) throw std::runtime_error("UNINSTALLER_IDENTITY_MISMATCH");
    if (!CanWrite(root)) {
        auto parameters = L"--worker --quiet --directory " + Quote(root) + L" --owner-sid " + Quote(options.owner);
        if (Run(Self(), parameters, true) != 0) throw std::runtime_error(Utf8(Text("Installer_Failed")));
        return;
    }
    CloseApplication(root);
    std::vector<fs::path> owned;
    std::set<fs::path> directories;
    {
        Handle lease(CreateFileW((fs::path(root) / L"._install_lock").c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
        Check(lease.value != INVALID_HANDLE_VALUE, "InstallationInProgress");
        Handle update(CreateFileW((fs::path(root) / L"._update_lock").c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
        Check(update.value != INVALID_HANDLE_VALUE, "UpdateInProgress");
        if (fs::exists(fs::path(root) / L"._install_backup") || fs::exists(fs::path(root) / L"._update_backup")) throw std::runtime_error("INSTALL_RECOVERY_REQUIRED");
        for (const auto& relative : ReadLines(fs::path(root) / Manifest)) {
            auto path = Owned(root, relative);
            auto top = fs::path(relative).begin()->wstring();
            if (top.rfind(L"._", 0) == 0 || _wcsicmp(top.c_str(), L"Uninstall.exe") == 0) throw std::runtime_error("UNINSTALL_INVALID_MANIFEST");
            owned.push_back(path);
            for (auto directory = path.parent_path(); directory != fs::path(root); directory = directory.parent_path()) directories.insert(directory);
        }
        Log(L"Uninstall start root=" + root + L" id=" + record.id);
        for (const auto& path : owned) if (fs::exists(path)) {
            if (!fs::is_regular_file(path)) throw std::runtime_error("UNINSTALL_FILE_IS_DIRECTORY");
            Check(DeleteFileW(path.c_str()) != 0, "DeletePayloadFile");
        }
        auto executable = (fs::path(root) / L"EricGameLauncher.exe").wstring();
        for (const auto& shortcut : {record.desktop, record.menu})
            if (!shortcut.empty() && ShortcutTargets(shortcut, executable)) Check(DeleteFileW(shortcut.c_str()) != 0, "DeleteShortcut");
        Check(DeleteFileW((fs::path(root) / L"Uninstall.exe").c_str()) != 0, "DeleteInstalledUninstaller");
        Check(DeleteFileW((fs::path(root) / Manifest).c_str()) != 0, "DeleteInstallManifest");
        RemoveRegistration(record);
    }
    DeleteFileW((fs::path(root) / L"._install_lock").c_str());
    DeleteFileW((fs::path(root) / L"._update_lock").c_str());
    std::vector<fs::path> sorted(directories.begin(), directories.end());
    std::sort(sorted.begin(), sorted.end(), [](const auto& a, const auto& b) { return a.wstring().size() > b.wstring().size(); });
    for (const auto& directory : sorted) if (fs::is_directory(directory) && fs::is_empty(directory)) fs::remove(directory);
    if (fs::is_empty(root)) fs::remove(root);
    Log(L"Uninstall complete; user data retained");
}
}
