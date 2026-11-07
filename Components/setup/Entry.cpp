#include "../setup.core/Installation.h"
#include <shlobj.h>
#include <iostream>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    bool quiet = false;
    try {
        setup::Check(SUCCEEDED(initialized), "CoInitializeEx");
        auto options = setup::Parse();
        quiet = options.quiet || options.update || options.stage;
        if (options.help) {
            AttachConsole(ATTACH_PARENT_PROCESS);
#ifdef EGL_UNINSTALL
            std::cout << setup::Utf8(setup::Text("Installer_UninstallHelp")) << std::endl;
#else
            std::cout << setup::Utf8(setup::Text("Installer_Help")) << std::endl;
#endif
            CoUninitialize();
            return 0;
        }
#ifdef EGL_UNINSTALL
        if (options.root.empty()) options.root = setup::fs::path(setup::Self()).parent_path().wstring();
        if (!setup::PrepareUninstallWorker(options)) { CoUninitialize(); return 0; }
        int result = 0;
        if (quiet) setup::Uninstall(options);
        else result = setup::Window(options, setup::Uninstall, true);
#else
        auto current = setup::ReadRegistration(options.owner);
        if (options.root.empty()) options.root = current ? current.root : (setup::fs::path(setup::Folder(FOLDERID_LocalAppData)) / L"Programs" / L"EricSoft" / L"EricGameLauncher").wstring();
        if (current) { options.desktop = !current.desktop.empty(); options.menu = !current.menu.empty(); }
        auto execute = [](const setup::Options& selected) { setup::Install(selected, setup::Self(), setup::Resource(102), EGL_VERSION); };
        int result = 0;
        auto operation = [execute](const setup::Options& selected) { if (selected.extract) setup::ExtractOnly(selected, setup::Self()); else execute(selected); };
        if (quiet) operation(options);
        else result = setup::Window(options, operation, false);
#endif
        CoUninitialize();
        return result;
    } catch (const std::exception& error) {
        auto message = setup::Wide(error.what());
        setup::Log(message);
        if (!quiet) MessageBoxW(nullptr, (setup::Text("Installer_Failed") + L"\n" + message).c_str(), L"Eric Game Launcher", MB_OK | MB_ICONERROR);
        if (SUCCEEDED(initialized)) CoUninitialize();
        return 1;
    }
}
