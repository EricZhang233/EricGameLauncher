#include "Installation.h"
#include "miniz.h"
#include <shlobj.h>
#include <fstream>
#include <set>
#include <algorithm>
#include <limits>

namespace setup {
struct CaseLess { bool operator()(const std::wstring& a, const std::wstring& b) const { return _wcsicmp(a.c_str(), b.c_str()) < 0; } };
struct RootCleanup {
    fs::path root;
    bool created;
    ~RootCleanup() {
        try {
            if (!created || !fs::exists(root)) return;
            std::vector<fs::path> directories;
            for (const auto& entry : fs::recursive_directory_iterator(root)) if (entry.is_directory()) { Owned(root, fs::relative(entry.path(), root).wstring()); directories.push_back(entry.path()); }
            std::sort(directories.begin(), directories.end(), [](const auto& a, const auto& b) { return a.wstring().size() > b.wstring().size(); });
            for (const auto& directory : directories) if (fs::is_empty(directory)) fs::remove(directory);
            if (fs::is_empty(root)) fs::remove(root);
        } catch (...) {}
    }
};
static fs::path PayloadPath(const fs::path& root, const std::wstring& relative) {
    auto path = Owned(root, relative);
    auto top = fs::path(relative).begin()->wstring();
    if (top.rfind(L"._", 0) == 0 || _wcsicmp(top.c_str(), L"Uninstall.exe") == 0) throw std::runtime_error("RESERVED_INSTALL_PATH");
    return path;
}
struct Archive {
    Handle file;
    mz_zip_archive zip{};
    explicit Archive(const std::wstring& path) : file(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr)) {
        Check(file.value != INVALID_HANDLE_VALUE, "OpenPackage");
        LARGE_INTEGER size{};
        Check(GetFileSizeEx(file.value, &size) != 0, "GetPackageSize");
        zip.m_pIO_opaque = file.value;
        zip.m_pRead = [](void* handle, mz_uint64 offset, void* buffer, size_t count) -> size_t {
            if (count > MAXDWORD) return 0;
            LARGE_INTEGER position{}; position.QuadPart = static_cast<LONGLONG>(offset);
            DWORD read = 0;
            if (!SetFilePointerEx(handle, position, nullptr, FILE_BEGIN) || !ReadFile(handle, buffer, static_cast<DWORD>(count), &read, nullptr)) return 0;
            return read;
        };
        Check(mz_zip_reader_init(&zip, static_cast<mz_uint64>(size.QuadPart), 0) != 0, "OpenZip");
    }
    ~Archive() { mz_zip_reader_end(&zip); }
};
static std::vector<std::wstring> Extract(const Options& options, const std::wstring& package, const fs::path& staging) {
    Archive archive(package);
    std::vector<std::wstring> files;
    std::set<std::wstring, CaseLess> names;
    mz_uint count = mz_zip_reader_get_num_files(&archive.zip);
    if (count > 100000) throw std::runtime_error("ZIP_TOO_MANY_FILES");
    mz_uint64 total = 0;
    for (mz_uint i = 0; i < count; ++i) {
        CheckCancelled(options);
        mz_zip_archive_file_stat stat{};
        Check(mz_zip_reader_file_stat(&archive.zip, i, &stat) != 0, "ZipFileStat");
        if (stat.m_is_directory) continue;
        if (stat.m_is_encrypted || (stat.m_method != 0 && stat.m_method != 8) || ((stat.m_external_attr >> 16) & 0170000) == 0120000) throw std::runtime_error("ZIP_UNSUPPORTED_ENTRY");
        auto length = mz_zip_reader_get_filename(&archive.zip, i, nullptr, 0);
        if (!length || length > 32768) throw std::runtime_error("ZIP_INVALID_FILENAME");
        std::string name(length, 0);
        Check(mz_zip_reader_get_filename(&archive.zip, i, name.data(), length) == length, "ZipFileName");
        name.pop_back();
        auto relative = Wide(name);
        std::replace(relative.begin(), relative.end(), L'/', L'\\');
        auto target = PayloadPath(staging, relative);
        relative = fs::relative(target, staging).wstring();
        if (!names.insert(relative).second) throw std::runtime_error("ZIP_DUPLICATE_ENTRY");
        total += stat.m_uncomp_size;
        if (stat.m_uncomp_size > 4ull * 1024 * 1024 * 1024 || total > 8ull * 1024 * 1024 * 1024) throw std::runtime_error("ZIP_TOO_LARGE");
        fs::create_directories(target.parent_path());
        Handle output(CreateFileW(target.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
        Check(output.value != INVALID_HANDLE_VALUE, "CreatePayloadFile");
        struct Output { HANDLE file; const Options* options; } context{output.value, &options};
        auto write = [](void* opaque, mz_uint64 offset, const void* buffer, size_t bytes) -> size_t {
            auto state = static_cast<Output*>(opaque);
            if ((state->options->cancellation && state->options->cancellation->load()) || (!state->options->cancelFile.empty() && GetFileAttributesW(state->options->cancelFile.c_str()) != INVALID_FILE_ATTRIBUTES)) return 0;
            HANDLE handle = state->file;
            if (bytes > MAXDWORD) return 0;
            LARGE_INTEGER position{}; position.QuadPart = static_cast<LONGLONG>(offset);
            DWORD written = 0;
            if (!SetFilePointerEx(handle, position, nullptr, FILE_BEGIN) || !WriteFile(handle, buffer, static_cast<DWORD>(bytes), &written, nullptr)) return 0;
            return written;
        };
        auto extracted = mz_zip_reader_extract_to_callback(&archive.zip, i, write, &context, 0);
        CheckCancelled(options);
        Check(extracted != 0, "ExtractPayloadCRC");
        Check(FlushFileBuffers(output.value) != 0, "FlushPayload");
        files.push_back(relative);
    }
    if (!names.contains(L"EricGameLauncher.exe")) throw std::runtime_error("PACKAGE_MISSING_MAIN");
    return files;
}
static void SafeRemoveTree(const fs::path& root, const fs::path& path) {
    if (!fs::exists(path)) return;
    Owned(root, fs::relative(path, root).wstring());
    for (const auto& entry : fs::recursive_directory_iterator(path)) Owned(root, fs::relative(entry.path(), root).wstring());
    fs::remove_all(path);
}
static void Append(const fs::path& journal, const std::wstring& line) {
    Handle output(CreateFileW(journal.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
    Check(output.value != INVALID_HANDLE_VALUE, "OpenJournal");
    std::string bytes = Utf8(line) + "\n";
    DWORD written = 0;
    Check(WriteFile(output.value, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) != 0 && written == bytes.size(), "WriteJournal");
    Check(FlushFileBuffers(output.value) != 0, "FlushJournal");
}
static void Recover(const fs::path& root, const fs::path& backup) {
    auto journal = ReadLines(backup / L"journal");
    if (std::find(journal.begin(), journal.end(), L"COMMIT") != journal.end()) return;
    for (const auto& line : journal) {
        if (line.size() < 3 || line[1] != L'\t' || (line[0] != L'B' && line[0] != L'I')) throw std::runtime_error("INVALID_INSTALL_JOURNAL");
        Owned(root, line.substr(2));
    }
    for (auto i = journal.rbegin(); i != journal.rend(); ++i) {
        auto relative = i->substr(2);
        auto target = Owned(root, relative);
        if ((*i)[0] == L'I') { if (fs::exists(target)) fs::remove(target); }
        else {
            auto original = Owned(backup / L"files", relative);
            if (fs::exists(original)) {
                fs::create_directories(target.parent_path());
                Check(CopyFileW(original.c_str(), target.c_str(), FALSE) != 0, "RestoreBackup");
            }
        }
    }
    Log(L"Recovered incomplete installation");
}
static void Prepare(const fs::path& root, const fs::path& staging, const fs::path& backup) {
    if (fs::exists(backup)) { Recover(root, backup); SafeRemoveTree(root, backup); }
    SafeRemoveTree(root, staging);
    fs::create_directory(staging);
}
static void Apply(const Options& options, const fs::path& root, const fs::path& staging, const fs::path& backup, const std::vector<std::wstring>& incoming, const std::vector<std::wstring>& previous, const std::function<void()>& complete) {
    fs::create_directories(backup / L"files");
    WriteLines(backup / L"journal", {});
    std::set<std::wstring, CaseLess> affected(previous.begin(), previous.end());
    affected.insert(incoming.begin(), incoming.end());
    for (const auto& relative : affected) {
        CheckCancelled(options);
        auto source = Owned(root, relative);
        if (!fs::exists(source)) continue;
        if (!fs::is_regular_file(source)) throw std::runtime_error("INSTALL_TARGET_IS_DIRECTORY");
        auto target = Owned(backup / L"files", relative);
        fs::create_directories(target.parent_path());
        Append(backup / L"journal", L"B\t" + relative);
        Check(MoveFileExW(source.c_str(), target.c_str(), MOVEFILE_WRITE_THROUGH) != 0, "BackupInstalledFile");
    }
    for (const auto& relative : incoming) {
        CheckCancelled(options);
        auto target = Owned(root, relative);
        fs::create_directories(target.parent_path());
        Append(backup / L"journal", L"I\t" + relative);
        Check(MoveFileExW((staging / relative).c_str(), target.c_str(), MOVEFILE_WRITE_THROUGH) != 0, "InstallPayloadFile");
    }
    CheckCancelled(options);
    complete();
    Append(backup / L"journal", L"COMMIT");
}
static void Cleanup(const fs::path& root, const fs::path& staging, const fs::path& backup, bool failed) {
    if (failed && fs::exists(backup)) Recover(root, backup);
    SafeRemoveTree(root, staging);
    SafeRemoveTree(root, backup);
}
void Finalize(const Options& options, const std::wstring& version) {
    auto root = ValidateRoot(options.root);
    Record previous = Find(root, options.owner);
    if (options.update && !previous) throw std::runtime_error("INSTALL_REGISTRATION_MISMATCH");
    Record record = previous;
    record.root = root;
    record.owner = options.owner;
    if (!record) {
        if (options.owner != Sid()) throw std::runtime_error("INSTALL_USER_MISMATCH");
        record.id = Guid();
        SYSTEMTIME date{}; GetLocalTime(&date);
        wchar_t value[9]{};
        swprintf_s(value, L"%04u%02u%02u", date.wYear, date.wMonth, date.wDay);
        record.date = value;
    }
    if (!previous || !options.update) {
        if (options.owner != Sid()) throw std::runtime_error("INSTALL_USER_MISMATCH");
        record.desktop = options.desktop ? (fs::path(Folder(FOLDERID_Desktop)) / L"EricGameLauncher.lnk").wstring() : L"";
        record.menu = (fs::path(Folder(FOLDERID_Programs)) / L"EricGameLauncher.lnk").wstring();
    }
    record.version = version;
    unsigned long long size = fs::file_size(fs::path(root) / L"Uninstall.exe");
    for (const auto& relative : ReadLines(fs::path(root) / Manifest)) size += fs::file_size(PayloadPath(root, relative));
    record.size = static_cast<DWORD>(std::min<unsigned long long>(size / 1024, MAXDWORD));
    std::map<std::wstring, std::wstring> backups;
    auto executable = (fs::path(root) / L"EricGameLauncher.exe").wstring();
    try {
        for (const auto& shortcut : {record.desktop, record.menu}) {
            if (shortcut.empty()) continue;
            std::wstring backup;
            if (fs::exists(shortcut)) {
                backup = shortcut + L"." + Guid() + L".bak";
                Check(CopyFileW(shortcut.c_str(), backup.c_str(), TRUE) != 0, "BackupShortcut");
            }
            backups.emplace(shortcut, backup);
            Shortcut(executable, shortcut);
        }
        WriteRegistration(record);
        for (const auto& shortcut : {previous.desktop, previous.menu})
            if (!shortcut.empty() && shortcut != record.desktop && shortcut != record.menu && ShortcutTargets(shortcut, executable)) fs::remove(shortcut);
    } catch (...) {
        for (const auto& [shortcut, backup] : backups) {
            if (backup.empty()) DeleteFileW(shortcut.c_str());
            else MoveFileExW(backup.c_str(), shortcut.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
        }
        try { if (previous) WriteRegistration(previous); else if (ReadRegistration(options.owner).id == record.id) RemoveRegistration(record); } catch (...) { Log(L"Registration rollback requires retry"); }
        throw;
    }
    for (const auto& [shortcut, backup] : backups) if (!backup.empty()) DeleteFileW(backup.c_str());
    Log(L"Registration committed id=" + record.id + L" version=" + version);
}
void Install(const Options& requested, const std::wstring& package, const std::vector<unsigned char>& uninstaller, const std::wstring& version) {
    Options options = requested;
    options.root = options.update || options.stage ? ValidateRoot(options.root) : Destination(options.root);
    auto root = options.root;
    auto registered = ReadRegistration(options.owner);
    auto previous = Find(root, options.owner);
    if (options.update && !previous) throw std::runtime_error("INSTALL_REGISTRATION_MISMATCH");
    if (registered && _wcsicmp(registered.root.c_str(), root.c_str()) != 0) throw std::runtime_error(Utf8(Text("Installer_AlreadyInstalled")));
    if (!CanWrite(root) && !options.stage) {
        auto parameters = L"--quiet --stage --directory " + Quote(root) + L" --owner-sid " + Quote(options.owner) + (options.update ? L" --update" : L"");
        if (!options.cancelFile.empty()) parameters += L" --cancel-file " + Quote(options.cancelFile);
        if (Run(package, parameters, true) != 0) { CheckCancelled(options); throw std::runtime_error(Utf8(Text("Installer_Failed"))); }
        Finalize(options, version);
        return;
    }
    if (!options.update) CloseApplication(root);
    Log(L"Install start root=" + root + L" owner=" + options.owner);
    RootCleanup cleanup{root, !fs::exists(root)};
    fs::create_directories(root);
    Handle lease(CreateFileW((fs::path(root) / L"._install_lock").c_str(), GENERIC_READ | GENERIC_WRITE | DELETE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
    Check(lease.value != INVALID_HANDLE_VALUE, "InstallationInProgress");
    Handle update(CreateFileW((fs::path(root) / L"._update_lock").c_str(), GENERIC_READ | GENERIC_WRITE | DELETE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
    Check(update.value != INVALID_HANDLE_VALUE, "UpdateInProgress");
    auto staging = fs::path(root) / L"._install_staging", backup = fs::path(root) / L"._install_backup";
    Prepare(root, staging, backup);
    try {
        auto incoming = Extract(options, package, staging);
        std::vector<std::wstring> previousFiles;
        if (fs::exists(fs::path(root) / Manifest)) previousFiles = ReadLines(fs::path(root) / Manifest);
        for (const auto& relative : previousFiles) PayloadPath(root, relative);
        for (const auto& relative : incoming) PayloadPath(root, relative);
        if (uninstaller.size() < 2 || uninstaller[0] != 'M' || uninstaller[1] != 'Z') throw std::runtime_error("INVALID_UNINSTALLER_RESOURCE");
        {
            std::ofstream output(staging / L"Uninstall.exe", std::ios::binary);
            output.write(reinterpret_cast<const char*>(uninstaller.data()), static_cast<std::streamsize>(uninstaller.size()));
            output.flush();
            if (!output) throw std::runtime_error("WRITE_UNINSTALLER_FAILED");
        }
        WriteLines(staging / Manifest, incoming);
        incoming.push_back(L"Uninstall.exe"); incoming.push_back(Manifest);
        Apply(options, root, staging, backup, incoming, previousFiles, [&] { if (!options.stage) Finalize(options, version); });
    } catch (...) {
        Cleanup(root, staging, backup, true);
        Log(L"Installation failed; files rolled back");
        throw;
    }
    Cleanup(root, staging, backup, false);
    Log(L"Installation complete version=" + version);
}
void ExtractOnly(const Options& requested, const std::wstring& package) {
    Options options = requested;
    options.root = Destination(options.root);
    auto root = options.root;
    if (!CanWrite(root)) {
        auto parameters = L"--quiet --extract --directory " + Quote(root) + L" --owner-sid " + Quote(options.owner);
        if (!options.cancelFile.empty()) parameters += L" --cancel-file " + Quote(options.cancelFile);
        auto result = Run(package, parameters, true);
        CheckCancelled(options);
        if (result != 0) throw std::runtime_error(Utf8(Text("Installer_Failed")));
        return;
    }
    bool existed = fs::exists(root);
    fs::create_directories(root);
    try {
        Handle lease(CreateFileW((fs::path(root) / L"._extract_lock").c_str(), GENERIC_READ | GENERIC_WRITE | DELETE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
        Check(lease.value != INVALID_HANDLE_VALUE, "ExtractionInProgress");
        auto staging = fs::path(root) / L"._extract_staging", backup = fs::path(root) / L"._extract_backup";
        Prepare(root, staging, backup);
        try {
            auto incoming = Extract(options, package, staging);
            Apply(options, root, staging, backup, incoming, {}, [] {});
        } catch (...) { Cleanup(root, staging, backup, true); throw; }
        Cleanup(root, staging, backup, false);
        Log(L"Extracted application payload only to " + root);
    } catch (...) {
        if (!existed && fs::exists(root) && fs::is_empty(root)) fs::remove(root);
        throw;
    }
}
}
