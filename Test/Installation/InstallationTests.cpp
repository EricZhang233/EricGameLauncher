#include "../../Components/setup.core/Installation.h"
#include "../../Components/setup/State.h"
#include <fstream>
#include <iostream>
#include <iterator>

using namespace setup;
static int checks = 0;
static void Assert(bool condition, const char* name) { if (!condition) throw std::runtime_error(name); std::cout << "PASS " << name << std::endl; ++checks; }
static std::string Read(const fs::path& path) { std::ifstream input(path, std::ios::binary); return { std::istreambuf_iterator<char>(input), {} }; }
static void Put(const fs::path& path, const std::string& text) { fs::create_directories(path.parent_path()); std::ofstream output(path, std::ios::binary); output << text; }
template<typename F> static bool Rejects(F function) { try { function(); return false; } catch (const std::exception&) { return true; } }
struct ApplicationFixture {
    Handle process;
    DWORD id = 0;
    explicit ApplicationFixture(const fs::path& executable) {
        fs::create_directories(executable.parent_path());
        Check(CopyFileW(Self().c_str(), executable.c_str(), FALSE) != 0, "CopyApplicationFixture");
        auto signal = L"Local\\EricGameLauncher.SetupTests.Ready." + Guid();
        Handle ready(CreateEventW(nullptr, TRUE, FALSE, signal.c_str()));
        Check(ready.value != nullptr, "CreateApplicationFixtureSignal");
        auto command = Quote(executable.wstring()) + L" --test-main-exit " + Quote(signal);
        STARTUPINFOW startup{sizeof(startup)};
        PROCESS_INFORMATION information{};
        Check(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr,
            executable.parent_path().c_str(), &startup, &information) != 0, "StartApplicationFixture");
        process.value = information.hProcess;
        id = information.dwProcessId;
        CloseHandle(information.hThread);
        if (WaitForSingleObject(ready.value, 5000) != WAIT_OBJECT_0) {
            TerminateProcess(process.value, 1);
            WaitForSingleObject(process.value, 5000);
            throw std::runtime_error("ApplicationFixtureNotReady");
        }
    }
    ~ApplicationFixture() {
        if (WaitForSingleObject(process.value, 0) != WAIT_TIMEOUT) return;
        EnumWindows([](HWND window, LPARAM value) -> BOOL {
            DWORD owner = 0; GetWindowThreadProcessId(window, &owner);
            if (owner == static_cast<DWORD>(value)) PostMessageW(window, WM_CLOSE, 0, 0);
            return TRUE;
        }, static_cast<LPARAM>(id));
        if (WaitForSingleObject(process.value, 5000) == WAIT_TIMEOUT) {
            TerminateProcess(process.value, 1);
            WaitForSingleObject(process.value, 5000);
        }
    }
};
static int RunApplicationFixture(const wchar_t* signal) {
    WNDCLASSW windowClass{};
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.lpszClassName = L"EricGameLauncher.SetupTests.Application";
    windowClass.lpfnWndProc = [](HWND window, UINT message, WPARAM wParam, LPARAM lParam) -> LRESULT {
        if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
        return DefWindowProcW(window, message, wParam, lParam);
    };
    if (!RegisterClassW(&windowClass)) return 2;
    HWND window = CreateWindowW(windowClass.lpszClassName, L"Setup test application", WS_OVERLAPPED,
        0, 0, 100, 100, nullptr, nullptr, windowClass.hInstance, nullptr);
    if (!window) return 2;
    Handle ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, signal));
    if (!ready.value || !SetEvent(ready.value)) return 2;
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return 0;
}

int wmain(int count, wchar_t** args) {
    if (count == 2 && std::wstring(args[1]) == L"--test-cli-exit") { Sleep(800); return 0; }
    if (count == 3 && std::wstring(args[1]) == L"--test-main-exit") return RunApplicationFixture(args[2]);
    if (count != 5) return 2;
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    HKEY actualUser = nullptr, sandbox = nullptr;
    std::wstring keyPath = L"Software\\EricSoft\\EricGameLauncher.SetupTests\\" + Guid();
    bool redirected = false;
    try {
        Check(SUCCEEDED(initialized), "CoInitializeEx");
        Check(RegOpenCurrentUser(KEY_ALL_ACCESS, &actualUser) == ERROR_SUCCESS, "OpenTestUser");
        Check(RegCreateKeyExW(actualUser, keyPath.c_str(), 0, nullptr, 0, KEY_ALL_ACCESS, nullptr, &sandbox, nullptr) == ERROR_SUCCESS, "CreateTestHive");
        Check(RegOverridePredefKey(HKEY_CURRENT_USER, sandbox) == ERROR_SUCCESS, "IsolateTestRegistry");
        redirected = true;
        OperationState state;
        Assert(state.phase == OperationPhase::Ready && !state.Pending() && !state.Terminal(), "initial operation is neutral and has no premature result");
        Assert(Rejects([&] { state.Finish(false, true); }), "result cannot be reported before an operation starts");
        state.Begin();
        Assert(state.Pending() && !state.Terminal() && state.phase == OperationPhase::Running, "operation stays pending until completion is delivered");
        state.Cancel();
        Assert(state.Pending() && !state.Terminal() && state.phase == OperationPhase::Cancelling, "cancellation waits for cleanup without reporting failure");
        state.Finish(false, true);
        Assert(state.phase == OperationPhase::Succeeded && state.Terminal(), "committed operation reports success only after completion");
        OperationState failed; failed.Begin(); failed.Finish(false, false);
        Assert(failed.phase == OperationPhase::Failed, "actual operation failure has an explicit terminal state");
        OperationState cancelled; cancelled.Begin(); cancelled.Cancel(); cancelled.Finish(true, false);
        Assert(cancelled.phase == OperationPhase::Cancelled && cancelled.Terminal(), "completed cancellation is distinct from failure");
        fs::path fixture(args[1]), first(args[2]), second(args[3]), unsafe(args[4]);
        Options options;
        options.owner = Sid(); options.root = (fixture / L"installed").wstring(); options.stage = true;
        options.dataDirectory = std::make_shared<fs::path>(fixture / L"system-data");
        Assert(!options.keepData, "uninstall defaults to removing application data");
        Assert(UninstallDataDirectory(Options{}) == fs::path(setup::Folder(FOLDERID_ProgramData)) / L"eric" / Product, "production uninstaller uses only the fixed system data directory");
        Put(*options.dataDirectory / L"settings.yaml", "settings");
        Put(*options.dataDirectory / L"items.yaml", "items");
        Put(*options.dataDirectory / L"ico" / L"game.png", "icon");
        auto fresh = options; fresh.extract = true;
        Assert(!SelectInstallation(fresh) && !fresh.update && !fresh.extract && fresh.root == options.root, "unregistered installation retains the normal path selection flow");
        Assert(!options.desktop && options.menu, "default shortcut choices match the wizard");
        Assert(Text("Installer_Welcome") != L"Installer_Welcome" && Text("Installer_Back") != L"Installer_Back", "native text resources load without an installed application");
        Assert(Rejects([] { ValidateRoot(L"C:\\"); }), "volume root is rejected");
        Assert(Destination((fixture / L"chosen").wstring()) == (fixture / L"chosen" / Product).wstring(), "chosen parent directory gains the application folder");
        Assert(Destination((fixture / L"chosen" / Product).wstring() + L"\\") == (fixture / L"chosen" / Product).wstring(), "existing application folder and trailing separator do not duplicate the name");
        Assert(Destination((fixture / L"ericgamelauncher").wstring()) == (fixture / L"ericgamelauncher").wstring(), "application folder matching follows Windows case insensitivity");
        Assert(Rejects([&] { Owned(options.root, L"..\\outside.txt"); }) && Rejects([&] { Owned(options.root, L"Data\\user.yaml"); }), "owned paths reject traversal and user data");
        auto bytes = Read(Self());
        std::vector<unsigned char> uninstaller(bytes.begin(), bytes.end());
        options.shortcuts = std::make_shared<ShortcutLocations>(ShortcutLocations{
            (fixture / L"shortcuts" / L"desktop.lnk").wstring(), (fixture / L"shortcuts" / L"menu.lnk").wstring()});
        Install(options, first.wstring(), uninstaller, EGL_VERSION);
        Assert(fs::exists(fs::path(options.root) / L"Uninstall.exe") && Find(options.root, options.owner), "elevated worker commits application files and registration together");
        Record record;
        record.root = options.root; record.owner = options.owner; record.id = Guid(); record.version = EGL_VERSION; record.date = L"20261108";
        record.menu = (fixture / L"shortcuts" / L"menu.lnk").wstring();
        record.desktop = (fixture / L"shortcuts" / L"desktop.lnk").wstring();
        options.shortcuts = std::make_shared<ShortcutLocations>(ShortcutLocations{record.desktop, record.menu});
        record.version = L"0.0.0";
        auto stored = record; stored.desktop.clear(); stored.menu.clear();
        WriteRegistration(stored);
        Put(record.desktop, "existing desktop shortcut");
        Put(record.menu, "existing Start menu shortcut");
        auto selection = options; selection.root = (fixture / L"ignored-choice").wstring();
        auto selected = SelectInstallation(selection);
        Assert(selected && selection.update && selection.root == record.root && selection.desktop, "registered installation selects upgrade and checks the physical desktop shortcut");
        auto summary = UpgradeSummary(selected, EGL_VERSION);
        Assert(summary.find(record.version) != std::wstring::npos && summary.find(EGL_VERSION) != std::wstring::npos && summary.find(record.root) == std::wstring::npos && summary.find(Text("Installer_StartMenu")) == std::wstring::npos, "upgrade confirmation contains version changes without path or shortcut details");
        fs::rename(fs::path(record.root) / L"Uninstall.exe", fs::path(record.root) / L"Uninstall.saved");
        Assert(!RegisteredInstallation(options.owner), "stale registration without an installed uninstaller does not enable upgrade");
        fs::rename(fs::path(record.root) / L"Uninstall.saved", fs::path(record.root) / L"Uninstall.exe");
        {
            Key registration;
            Check(RegOpenKeyExW(HKEY_CURRENT_USER, UninstallKey, 0, KEY_READ, &registration.value) == ERROR_SUCCESS, "OpenRegistration");
            wchar_t name[256]{}; DWORD size = sizeof(name);
            Check(RegGetValueW(registration.value, nullptr, L"DisplayName", RRF_RT_REG_SZ, nullptr, name, &size) == ERROR_SUCCESS, "ReadDisplayName");
            Assert(std::wstring(name) == Product, "registered application name matches EricGameLauncher exactly");
            wchar_t quiet[32768]{}; DWORD quietSize = sizeof(quiet);
            Check(RegGetValueW(registration.value, nullptr, L"QuietUninstallString", RRF_RT_REG_SZ, nullptr, quiet, &quietSize) == ERROR_SUCCESS, "ReadQuietUninstall");
            Assert(std::wstring(quiet).find(L"--quiet --remove-data") != std::wstring::npos, "registered quiet uninstall specifies its data deletion policy explicitly");
        }
        options.stage = false; options.update = true;
        Finalize(options, EGL_VERSION);
        Assert(ShortcutTargets(record.desktop, (fs::path(options.root) / L"EricGameLauncher.exe").wstring()), "upgrade refreshes an existing desktop shortcut despite an empty registry preference");
        Assert(Find(options.root, options.owner).id == record.id, "registration records the matching root and installation identity");
        Assert(!Find((fixture / L"other").wstring(), options.owner), "uninstaller presence alone does not identify a registered root");
        Assert(ShortcutTargets(record.menu, (fs::path(options.root) / L"EricGameLauncher.exe").wstring()), "native shortcut targets the installed executable");
        Put(fs::path(options.root) / L"Data" / L"keep.txt", "data");
        Put(fs::path(options.root) / L"unknown.txt", "unknown");
        auto before = Read(fs::path(options.root) / L"EricGameLauncher.exe");
        {
            Handle locked(CreateFileW((fs::path(options.root) / L"obsolete.dll").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
            Check(locked.value != INVALID_HANDLE_VALUE, "LockFixture");
            Assert(Rejects([&] { Install(options, second.wstring(), uninstaller, EGL_VERSION); }), "locked payload fails the installation");
        }
        Assert(Read(fs::path(options.root) / L"EricGameLauncher.exe") == before && fs::exists(fs::path(options.root) / L"obsolete.dll"), "failed installation rolls existing files back");
        options.cancellation = std::make_shared<std::atomic_bool>(true);
        Assert(Rejects([&] { Install(options, second.wstring(), uninstaller, EGL_VERSION); }), "cancelled installation is rejected");
        Assert(Read(fs::path(options.root) / L"EricGameLauncher.exe") == before && !fs::exists(fs::path(options.root) / L"._install_staging") && !fs::exists(fs::path(options.root) / L"._install_backup"), "cancellation preserves installed files and removes temporary state");
        options.cancellation.reset();
        Assert(Rejects([&] { Install(options, unsafe.wstring(), uninstaller, EGL_VERSION); }), "archive traversal is rejected before applying files");
        {
            auto elevated = options; elevated.stage = true;
            auto oldManifest = Read(fs::path(options.root) / Manifest);
            auto oldVersion = Find(options.root, options.owner).version;
            Handle occupied(CreateFileW(record.menu.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
            Check(occupied.value != INVALID_HANDLE_VALUE, "OccupyWorkerShortcut");
            Assert(Rejects([&] { Install(elevated, second.wstring(), uninstaller, L"9.9.9"); }), "elevated worker rejects failed shortcut registration before committing files");
            Assert(Read(fs::path(options.root) / L"EricGameLauncher.exe") == before
                && Read(fs::path(options.root) / Manifest) == oldManifest && fs::exists(fs::path(options.root) / L"obsolete.dll")
                && Find(options.root, options.owner).version == oldVersion,
                "elevated registration failure restores old payload, manifest and registered version");
        }
        fs::remove(record.desktop);
        Put(record.menu, "replace this Start menu entry");
        options.desktop = true;
        {
            ApplicationFixture target(fs::path(options.root) / L"EricGameLauncher.exe");
            ApplicationFixture unrelated(fixture / L"another-copy" / L"EricGameLauncher.exe");
            Assert(WaitForSingleObject(target.process.value, 0) == WAIT_TIMEOUT, "target application is running before a direct installer upgrade");
            Install(options, second.wstring(), uninstaller, EGL_VERSION);
            DWORD exitCode = 1;
            Assert(WaitForSingleObject(target.process.value, 0) == WAIT_OBJECT_0
                && GetExitCodeProcess(target.process.value, &exitCode) && exitCode == 0,
                "direct installer upgrade gracefully closes the target application before replacing its executable");
            Assert(WaitForSingleObject(unrelated.process.value, 0) == WAIT_TIMEOUT,
                "installer upgrade leaves an application in another directory running");
        }
        auto updated = Find(options.root, options.owner);
        Assert(updated.id == record.id && updated.desktop.empty() && !fs::exists(record.desktop), "upgrade does not recreate a missing desktop shortcut despite a saved registry path");
        Assert(updated.menu == record.menu && ShortcutTargets(record.menu, (fs::path(options.root) / L"EricGameLauncher.exe").wstring()), "upgrade always replaces the actual Start menu registration");
        auto checked = options; checked.update = false; checked.desktop = true;
        Finalize(checked, EGL_VERSION);
        Assert(fs::exists(record.desktop), "new installation honors the selected desktop checkbox");
        checked.desktop = false;
        Finalize(checked, EGL_VERSION);
        Assert(!fs::exists(record.desktop) && fs::exists(record.menu), "new installation can omit desktop while retaining the mandatory Start menu entry");
        Assert(!fs::exists(fs::path(options.root) / L"obsolete.dll") && fs::exists(fs::path(options.root) / L"feature" / L"new.dll"), "native update removes obsolete owned files and installs new files");
        Assert(Read(fs::path(options.root) / L"Data" / L"keep.txt") == "data" && Read(fs::path(options.root) / L"unknown.txt") == "unknown", "update preserves data and unknown files");
        auto manifest = ReadLines(fs::path(options.root) / Manifest);
        auto invalid = manifest; invalid.push_back(L"..\\outside.txt");
        WriteLines(fs::path(options.root) / Manifest, invalid);
        Assert(Rejects([&] { Uninstall(options); }) && fs::exists(fs::path(options.root) / L"EricGameLauncher.exe"), "uninstall validates the complete manifest before deleting any payload");
        WriteLines(fs::path(options.root) / Manifest, manifest);
        for (const auto& lockName : {L"._install_lock", L"._update_lock"}) {
            Handle occupied(CreateFileW((fs::path(options.root) / lockName).c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, 0, nullptr));
            Check(occupied.value != INVALID_HANDLE_VALUE, "OccupyUninstallOperation");
            Assert(Rejects([&] { Uninstall(options); }) && fs::exists(*options.dataDirectory / L"settings.yaml"), "busy installation or update blocks uninstall before touching data");
        }
        {
            auto original = Read(fs::path(options.root) / L"EricGameLauncher.exe");
            auto relative = manifest.back();
            Handle occupied(CreateFileW(Owned(options.root, relative).c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
            Check(occupied.value != INVALID_HANDLE_VALUE, "OccupyUninstallPayload");
            Assert(Rejects([&] { Uninstall(options); }), "occupied payload rejects uninstall before deleting application data");
            Assert(Read(fs::path(options.root) / L"EricGameLauncher.exe") == original && Find(options.root, options.owner)
                && fs::exists(*options.dataDirectory / L"settings.yaml") && !fs::exists(fs::path(options.root) / L"._uninstall_backup"),
                "failed uninstall restores staged files and retains data and registration");
        }
        {
            Handle occupied(CreateFileW((*options.dataDirectory / L"settings.yaml").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
            Check(occupied.value != INVALID_HANDLE_VALUE, "OccupyUninstallData");
            Assert(Rejects([&] { Uninstall(options); }) && Find(options.root, options.owner)
                && fs::exists(*options.dataDirectory / L"settings.yaml"), "occupied data rejects uninstall and restores application identity");
        }
        {
            Key startup;
            Check(RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, nullptr, 0, KEY_ALL_ACCESS, nullptr, &startup.value, nullptr) == ERROR_SUCCESS, "CreateRollbackStartup");
            auto command = Quote((fs::path(options.root) / L"EricGameLauncher.exe").wstring()) + L" -service";
            Check(RegSetValueExW(startup.value, Product, 0, REG_SZ, reinterpret_cast<const BYTE*>(command.c_str()), static_cast<DWORD>((command.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS, "WriteRollbackStartup");
            options.checkpoint = [](const std::string& point) { if (point == "uninstall-registration-removed") throw std::runtime_error("INJECTED_UNINSTALL_FAILURE"); };
            Assert(Rejects([&] { Uninstall(options); }), "uninstall metadata failure rolls back before data deletion");
            options.checkpoint = {};
            Assert(Find(options.root, options.owner).startup == command && fs::exists(*options.dataDirectory / L"settings.yaml")
                && ShortcutTargets(record.menu, (fs::path(options.root) / L"EricGameLauncher.exe").wstring()),
                "uninstall rollback restores shortcuts, startup entry, registration and data");
        }
        checked.desktop = true;
        Finalize(checked, EGL_VERSION);
        Put(fixture / L"other.exe", "other");
        Shortcut((fixture / L"other.exe").wstring(), record.desktop);
        auto cli = fs::path(options.root) / L"EricGameLauncher.Cli.exe";
        fs::copy_file(Self(), cli, fs::copy_options::overwrite_existing);
        std::wstring command = Quote(cli.wstring()) + L" --test-cli-exit";
        STARTUPINFOW startup{ sizeof(startup) };
        PROCESS_INFORMATION process{};
        Check(CreateProcessW(cli.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, options.root.c_str(), &startup, &process) != 0, "StartInstalledCliFixture");
        Handle cliProcess(process.hProcess), cliThread(process.hThread);
        Assert(WaitForSingleObject(cliProcess.value, 0) == WAIT_TIMEOUT, "CLI fixture is still running before uninstall preparation");
        CloseApplication(options.root);
        Assert(WaitForSingleObject(cliProcess.value, 0) == WAIT_OBJECT_0, "uninstall preparation waits for the installed CLI to release application files");
        options.keepData = true;
        Uninstall(options);
        Assert(!ReadRegistration(options.owner) && !fs::exists(fs::path(options.root) / L"Uninstall.exe"), "uninstall removes its system registration and installed uninstaller");
        Assert(fs::exists(record.desktop) && !fs::exists(record.menu), "uninstall preserves shortcuts replaced by another target");
        Assert(Read(fs::path(options.root) / L"Data" / L"keep.txt") == "data" && Read(fs::path(options.root) / L"unknown.txt") == "unknown", "uninstall retains user data and unknown files");
        Assert(Read(*options.dataDirectory / L"settings.yaml") == "settings" && Read(*options.dataDirectory / L"items.yaml") == "items" && Read(*options.dataDirectory / L"ico" / L"game.png") == "icon", "keep-data retains fixed system settings, items and icons");
        options.root = (fixture / L"installed-remove").wstring();
        options.keepData = false; options.update = false; options.stage = true;
        Install(options, second.wstring(), uninstaller, EGL_VERSION);
        options.stage = false;
        Finalize(options, EGL_VERSION);
        Put(fs::path(options.root) / L"Data" / L"keep.txt", "portable data is outside the fixed system store");
        Put(fs::path(options.root) / L"storage.mode", "portable");
        Put(fs::path(options.root) / L"unknown.txt", "unknown");
        auto unsafeData = options;
        unsafeData.dataDirectory = std::make_shared<fs::path>(fixture);
        Assert(Rejects([&] { Uninstall(unsafeData); }) && fs::exists(fs::path(options.root) / L"EricGameLauncher.exe") && Read(*options.dataDirectory / L"settings.yaml") == "settings", "data cleanup rejects an ancestor of the installation before deleting files");
        auto external = fixture / L"external-data";
        Put(external / L"keep.txt", "outside data directory");
        bool linked = CreateSymbolicLinkW((*options.dataDirectory / L"external-link").c_str(), external.c_str(), SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE) != 0;
        Uninstall(options);
        Assert(!fs::exists(*options.dataDirectory) && !ReadRegistration(options.owner), "remove-data deletes the fixed system data tree and completes uninstallation");
        Assert(Read(fs::path(options.root) / L"Data" / L"keep.txt") == "portable data is outside the fixed system store" && Read(fs::path(options.root) / L"storage.mode") == "portable" && Read(fs::path(options.root) / L"unknown.txt") == "unknown", "uninstall ignores portable preferences and leaves unrelated installation-directory files intact");
        if (linked) Assert(Read(external / L"keep.txt") == "outside data directory", "data cleanup removes links without following them outside the fixed data store");
        Options extract;
        extract.owner = options.owner; extract.root = (fixture / L"portable").wstring(); extract.extract = true;
        ExtractOnly(extract, first.wstring());
        extract.root = Destination(extract.root);
        Assert(fs::exists(fs::path(extract.root) / L"EricGameLauncher.exe") && !fs::exists(fs::path(extract.root) / L"Uninstall.exe") && !fs::exists(fs::path(extract.root) / Manifest) && !ReadRegistration(options.owner), "extraction produces application files without registration or an uninstaller");
        for (const auto& lockName : {L"._install_lock", L"._update_lock"}) {
            auto beforeExtract = Read(fs::path(extract.root) / L"EricGameLauncher.exe");
            Handle occupied(CreateFileW((fs::path(extract.root) / lockName).c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, 0, nullptr));
            Check(occupied.value != INVALID_HANDLE_VALUE, "OccupyExtractOperation");
            Assert(Rejects([&] { ExtractOnly(extract, second.wstring()); })
                && Read(fs::path(extract.root) / L"EricGameLauncher.exe") == beforeExtract
                && !fs::exists(fs::path(extract.root) / L"._extract_staging"),
                "installer extraction honors installation and update locks before staging or replacing files");
        }
        {
            auto blocked = extract; blocked.stage = true; blocked.root = (fixture / L"not-writable" / Product).wstring();
            Put(blocked.root, "not a writable directory");
            bool stopped = false;
            try { ExtractOnly(blocked, L"fixture-package-that-does-not-exist.exe"); }
            catch (const std::exception& error) { stopped = error.what() == Utf8(Text("Installer_DirectoryNotWritable")); }
            Assert(stopped, "unwritable elevated extraction fails without another elevation attempt");
        }
        extract.cancellation = std::make_shared<std::atomic_bool>(true);
        extract.root = (fixture / L"cancelled-extraction").wstring();
        Assert(Rejects([&] { ExtractOnly(extract, first.wstring()); }) && !fs::exists(Destination(extract.root)), "cancelled fresh extraction removes its directory and temporary files");
        std::cout << checks << " checks passed." << std::endl;
        RegOverridePredefKey(HKEY_CURRENT_USER, nullptr); redirected = false;
        RegCloseKey(sandbox); sandbox = nullptr;
        RegDeleteTreeW(actualUser, keyPath.c_str()); RegCloseKey(actualUser); actualUser = nullptr;
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << std::endl;
        if (redirected) RegOverridePredefKey(HKEY_CURRENT_USER, nullptr);
        if (sandbox) RegCloseKey(sandbox);
        if (actualUser) { RegDeleteTreeW(actualUser, keyPath.c_str()); RegCloseKey(actualUser); }
        if (SUCCEEDED(initialized)) CoUninitialize();
        return 1;
    }
}
