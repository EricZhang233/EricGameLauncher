#include "../../Components/setup.core/Installation.h"
#include <fstream>
#include <iostream>
#include <iterator>

using namespace setup;
static int checks = 0;
static void Assert(bool condition, const char* name) { if (!condition) throw std::runtime_error(name); std::cout << "PASS " << name << std::endl; ++checks; }
static std::string Read(const fs::path& path) { std::ifstream input(path, std::ios::binary); return { std::istreambuf_iterator<char>(input), {} }; }
static void Put(const fs::path& path, const std::string& text) { fs::create_directories(path.parent_path()); std::ofstream output(path, std::ios::binary); output << text; }
template<typename F> static bool Rejects(F function) { try { function(); return false; } catch (const std::exception&) { return true; } }

int wmain(int count, wchar_t** args) {
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
        fs::path fixture(args[1]), first(args[2]), second(args[3]), unsafe(args[4]);
        Options options;
        options.owner = Sid(); options.root = (fixture / L"installed").wstring(); options.stage = true;
        Assert(!options.desktop && options.menu, "default shortcut choices match the wizard");
        Assert(Text("Installer_Welcome") != L"Installer_Welcome" && Text("Installer_Back") != L"Installer_Back", "native text resources load without an installed application");
        Assert(Rejects([] { ValidateRoot(L"C:\\"); }), "volume root is rejected");
        Assert(Destination((fixture / L"chosen").wstring()) == (fixture / L"chosen" / Product).wstring(), "chosen parent directory gains the application folder");
        Assert(Destination((fixture / L"chosen" / Product).wstring() + L"\\") == (fixture / L"chosen" / Product).wstring(), "existing application folder and trailing separator do not duplicate the name");
        Assert(Destination((fixture / L"ericgamelauncher").wstring()) == (fixture / L"ericgamelauncher").wstring(), "application folder matching follows Windows case insensitivity");
        Assert(Rejects([&] { Owned(options.root, L"..\\outside.txt"); }) && Rejects([&] { Owned(options.root, L"Data\\user.yaml"); }), "owned paths reject traversal and user data");
        auto bytes = Read(Self());
        std::vector<unsigned char> uninstaller(bytes.begin(), bytes.end());
        Install(options, first.wstring(), uninstaller, EGL_VERSION);
        Assert(fs::exists(fs::path(options.root) / L"Uninstall.exe") && !Find(options.root, options.owner), "file staging writes the uninstaller without registering another user");
        Record record;
        record.root = options.root; record.owner = options.owner; record.id = Guid(); record.version = EGL_VERSION; record.date = L"20261108";
        record.menu = (fixture / L"shortcuts" / L"menu.lnk").wstring();
        record.desktop = (fixture / L"shortcuts" / L"desktop.lnk").wstring();
        WriteRegistration(record);
        options.stage = false; options.update = true;
        Finalize(options, EGL_VERSION);
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
        Install(options, second.wstring(), uninstaller, EGL_VERSION);
        auto updated = Find(options.root, options.owner);
        Assert(updated.id == record.id && updated.desktop == record.desktop && updated.menu == record.menu, "silent update preserves installation identity and shortcut choices");
        Assert(!fs::exists(fs::path(options.root) / L"obsolete.dll") && fs::exists(fs::path(options.root) / L"feature" / L"new.dll"), "native update removes obsolete owned files and installs new files");
        Assert(Read(fs::path(options.root) / L"Data" / L"keep.txt") == "data" && Read(fs::path(options.root) / L"unknown.txt") == "unknown", "update preserves data and unknown files");
        auto manifest = ReadLines(fs::path(options.root) / Manifest);
        auto invalid = manifest; invalid.push_back(L"..\\outside.txt");
        WriteLines(fs::path(options.root) / Manifest, invalid);
        Assert(Rejects([&] { Uninstall(options); }) && fs::exists(fs::path(options.root) / L"EricGameLauncher.exe"), "uninstall validates the complete manifest before deleting any payload");
        WriteLines(fs::path(options.root) / Manifest, manifest);
        Put(fixture / L"other.exe", "other");
        Shortcut((fixture / L"other.exe").wstring(), record.desktop);
        Uninstall(options);
        Assert(!ReadRegistration(options.owner) && !fs::exists(fs::path(options.root) / L"Uninstall.exe"), "uninstall removes its system registration and installed uninstaller");
        Assert(fs::exists(record.desktop) && !fs::exists(record.menu), "uninstall preserves shortcuts replaced by another target");
        Assert(Read(fs::path(options.root) / L"Data" / L"keep.txt") == "data" && Read(fs::path(options.root) / L"unknown.txt") == "unknown", "uninstall retains user data and unknown files");
        Options extract;
        extract.owner = options.owner; extract.root = (fixture / L"portable").wstring(); extract.extract = true;
        ExtractOnly(extract, first.wstring());
        extract.root = Destination(extract.root);
        Assert(fs::exists(fs::path(extract.root) / L"EricGameLauncher.exe") && !fs::exists(fs::path(extract.root) / L"Uninstall.exe") && !fs::exists(fs::path(extract.root) / Manifest) && !ReadRegistration(options.owner), "extraction produces application files without registration or an uninstaller");
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
