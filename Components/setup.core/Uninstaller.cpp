#include "Installation.h"
#include <bcrypt.h>
#include <set>
#include <algorithm>

namespace setup {
static void ValidateDataDirectory(const fs::path& directory, const fs::path& installation) {
    auto data = directory.wstring();
    auto root = fs::absolute(installation).lexically_normal().make_preferred().wstring();
    if (directory == directory.root_path() || _wcsicmp(data.c_str(), root.c_str()) == 0
        || (root.size() > data.size() && _wcsnicmp(root.c_str(), data.c_str(), data.size()) == 0 && root[data.size()] == L'\\'))
        throw std::runtime_error(Utf8(Text("Installer_UninstallDataUnsafe")));
}
static std::wstring DataMutexName(const fs::path& directory) {
    auto value = directory.wstring();
    int length = LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_UPPERCASE, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr, 0);
    Check(length > 0, "DataDirectoryCase");
    std::wstring upper(length, L'\0');
    Check(LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_UPPERCASE, value.c_str(), static_cast<int>(value.size()), upper.data(), length, nullptr, nullptr, 0) == length, "DataDirectoryCase");
    auto input = Utf8(upper);
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0, "DataHashProvider");
    unsigned char digest[32]{};
    auto result = BCryptHash(algorithm, nullptr, 0, reinterpret_cast<PUCHAR>(input.data()), static_cast<ULONG>(input.size()), digest, sizeof(digest));
    BCryptCloseAlgorithmProvider(algorithm, 0);
    Check(result == 0, "DataDirectoryHash");
    std::wstring name = L"Local\\EricGameLauncher.Data.";
    constexpr wchar_t digits[] = L"0123456789ABCDEF";
    for (unsigned char byte : digest) { name += digits[byte >> 4]; name += digits[byte & 15]; }
    return name;
}
struct DataLease {
    Handle mutex;
    explicit DataLease(const fs::path& directory) : mutex(CreateMutexW(nullptr, FALSE, DataMutexName(directory).c_str())) {
        Check(mutex.value != nullptr, "DataDirectoryMutex");
        DWORD result = WaitForSingleObject(mutex.value, 20000);
        Check(result == WAIT_OBJECT_0 || result == WAIT_ABANDONED, "DataDirectoryBusy");
    }
    ~DataLease() { ReleaseMutex(mutex.value); }
};
static bool DataNeedsElevation(const fs::path& directory) {
    DWORD attributes = GetFileAttributesW(directory.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) return GetLastError() == ERROR_ACCESS_DENIED;
    if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return !CanWrite(directory.parent_path());
    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0) return !CanWrite(directory.parent_path());
    return !CanWrite(directory);
}
static void DeleteDataTree(const fs::path& directory) {
    DWORD attributes = GetFileAttributesW(directory.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        DWORD error = GetLastError();
        Check(error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND, "ReadApplicationData");
        return;
    }
    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
        for (const auto& entry : fs::directory_iterator(directory)) DeleteDataTree(entry.path());
    if ((attributes & FILE_ATTRIBUTE_READONLY) != 0 && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
        Check(SetFileAttributesW(directory.c_str(), attributes & ~FILE_ATTRIBUTE_READONLY) != 0, "WritableApplicationData");
    Check(((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ? RemoveDirectoryW(directory.c_str()) : DeleteFileW(directory.c_str())) != 0, "DeleteApplicationData");
}
static void CheckDataDeletion(const fs::path& directory, std::vector<std::unique_ptr<Handle>>& handles) {
    DWORD attributes = GetFileAttributesW(directory.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        DWORD error = GetLastError();
        Check(error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND, "ReadApplicationData");
        return;
    }
    DWORD access = DELETE | FILE_READ_ATTRIBUTES;
    if ((attributes & FILE_ATTRIBUTE_READONLY) != 0 && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0) access |= FILE_WRITE_ATTRIBUTES;
    auto handle = std::make_unique<Handle>(CreateFileW(directory.c_str(), access, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    Check(handle->value != INVALID_HANDLE_VALUE, "ApplicationDataInUse");
    handles.push_back(std::move(handle));
    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
        for (const auto& entry : fs::directory_iterator(directory)) CheckDataDeletion(entry.path(), handles);
}
static void RemoveBackup(const fs::path& root, const fs::path& backup) {
    if (!fs::exists(backup)) return;
    Owned(root, fs::relative(backup, root).wstring());
    for (const auto& entry : fs::recursive_directory_iterator(backup)) Owned(root, fs::relative(entry.path(), root).wstring());
    fs::remove_all(backup);
}
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
    auto parameters = L"--worker --directory " + Quote(record.root) + L" --owner-sid " + Quote(options.owner) + L" --parent " + std::to_wstring(GetCurrentProcessId())
        + (options.keepData ? L" --keep-data" : L" --remove-data") + (options.quiet ? L" --quiet" : L"");
    Run(worker.wstring(), parameters, false, false);
    Log(L"Uninstall handed off to temporary worker");
    return false;
}
void Uninstall(const Options& options) {
    auto root = ValidateRoot(options.root);
    auto record = Find(root, options.owner);
    if (!record) throw std::runtime_error("INSTALL_REGISTRATION_MISMATCH");
    if (Hash(fs::path(root) / L"Uninstall.exe") != Hash(Self())) throw std::runtime_error("UNINSTALLER_IDENTITY_MISMATCH");
    auto data = UninstallDataDirectory(options);
    if (!options.keepData) ValidateDataDirectory(data, root);
    if (!CanWrite(root) || (!options.keepData && DataNeedsElevation(data))) {
        Check(IsUserAnAdmin() == FALSE, "UninstallDirectoryNotWritable");
        auto parameters = L"--worker --quiet --directory " + Quote(root) + L" --owner-sid " + Quote(options.owner) + (options.keepData ? L" --keep-data" : L" --remove-data");
        if (Run(Self(), parameters, true) != 0) throw std::runtime_error(Utf8(Text("Installer_Failed")));
        return;
    }
    CloseApplication(root);
    std::vector<fs::path> owned;
    std::set<fs::path> directories;
    {
        DirectoryLease lease(root);
        auto current = Find(root, options.owner);
        if (!current || current.id != record.id || Hash(fs::path(root) / L"Uninstall.exe") != Hash(Self())) throw std::runtime_error("UNINSTALLER_IDENTITY_MISMATCH");
        record = current;
        auto backup = Owned(root, L"._uninstall_backup");
        if (fs::exists(backup) || fs::exists(fs::path(root) / L"._install_backup") || fs::exists(fs::path(root) / L"._update_backup")) throw std::runtime_error("INSTALL_RECOVERY_REQUIRED");
        for (const auto& relative : ReadLines(fs::path(root) / Manifest)) {
            auto path = Owned(root, relative);
            auto top = fs::path(relative).begin()->wstring();
            if (top.rfind(L"._", 0) == 0 || _wcsicmp(top.c_str(), L"Uninstall.exe") == 0) throw std::runtime_error("UNINSTALL_INVALID_MANIFEST");
            owned.push_back(path);
            for (auto directory = path.parent_path(); directory != fs::path(root); directory = directory.parent_path()) directories.insert(directory);
        }
        Log(L"Uninstall start root=" + root + L" id=" + record.id);
        owned.push_back(fs::path(root) / L"Uninstall.exe");
        owned.push_back(fs::path(root) / Manifest);
        auto executable = (fs::path(root) / L"EricGameLauncher.exe").wstring();
        std::vector<std::pair<fs::path, fs::path>> staged, shortcuts;
        std::unique_ptr<DataLease> dataLease;
        std::vector<std::unique_ptr<Handle>> dataHandles;
        bool registrationChanged = false;
        fs::create_directory(backup);
        try {
            std::vector<std::wstring> plan;
            for (const auto& path : owned) plan.push_back(fs::relative(path, root).wstring());
            WriteLines(backup / L"files.list", plan);
            WriteLines(backup / L"registration", {record.root, record.id, record.owner, record.version, record.date,
                record.desktop, record.menu, std::to_wstring(record.size), record.startup});
            for (const auto& path : owned) if (fs::exists(path)) {
                if (!fs::is_regular_file(path)) throw std::runtime_error("UNINSTALL_FILE_IS_DIRECTORY");
                auto saved = Owned(backup / L"files", fs::relative(path, root).wstring());
                fs::create_directories(saved.parent_path());
                staged.emplace_back(path, saved);
                Check(MoveFileExW(path.c_str(), saved.c_str(), MOVEFILE_WRITE_THROUGH) != 0, "StageUninstallFile");
            }
            for (const auto& shortcut : {record.desktop, record.menu}) {
                if (shortcut.empty() || !ShortcutTargets(shortcut, executable)) continue;
                auto saved = backup / (L"shortcut-" + std::to_wstring(shortcuts.size()) + L".lnk");
                Check(CopyFileW(shortcut.c_str(), saved.c_str(), TRUE) != 0, "BackupUninstallShortcut");
                Check(DeleteFileW(shortcut.c_str()) != 0, "DeleteShortcut");
                shortcuts.emplace_back(shortcut, saved);
            }
            if (!options.keepData) {
                dataLease = std::make_unique<DataLease>(data);
                CheckDataDeletion(data, dataHandles);
            }
            if (options.checkpoint) options.checkpoint("uninstall-files-staged");
            registrationChanged = true;
            RemoveRegistration(record);
            if (options.checkpoint) options.checkpoint("uninstall-registration-removed");
            WriteLines(backup / L"commit", {L"COMMIT"});
        } catch (...) {
            auto failure = std::current_exception();
            bool restored = true;
            for (auto item = staged.rbegin(); item != staged.rend(); ++item) {
                if (GetFileAttributesW(item->second.c_str()) != INVALID_FILE_ATTRIBUTES
                    && !MoveFileExW(item->second.c_str(), item->first.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) restored = false;
            }
            for (const auto& item : shortcuts)
                if (!CopyFileW(item.second.c_str(), item.first.c_str(), FALSE)) restored = false;
            if (registrationChanged) {
                try { RestoreRegistration(record); }
                catch (...) { restored = false; }
            }
            if (restored) {
                try { RemoveBackup(root, backup); }
                catch (...) { Log(L"Uninstall rollback backup remains at " + backup.wstring()); }
            } else Log(L"Uninstall rollback requires recovery from " + backup.wstring());
            Log(L"Uninstall failed before commit; application data retained");
            std::rethrow_exception(failure);
        }
        dataHandles.clear();
        if (!options.keepData) DeleteDataTree(data);
        RemoveBackup(root, backup);
    }
    DeleteFileW((fs::path(root) / L"._install_lock").c_str());
    DeleteFileW((fs::path(root) / L"._update_lock").c_str());
    std::vector<fs::path> sorted(directories.begin(), directories.end());
    std::sort(sorted.begin(), sorted.end(), [](const auto& a, const auto& b) { return a.wstring().size() > b.wstring().size(); });
    for (const auto& directory : sorted) if (fs::is_directory(directory) && fs::is_empty(directory)) fs::remove(directory);
    if (fs::is_empty(root)) fs::remove(root);
    Log(options.keepData ? L"Uninstall complete; application data retained" : L"Uninstall complete; application data removed");
}
}
