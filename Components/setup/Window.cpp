#include "../setup.core/Installation.h"
#include <commctrl.h>
#include <prsht.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <array>
#include <thread>

namespace setup {
inline constexpr int WizardBack = 0x3023, WizardNext = 0x3024, WizardFinish = 0x3025;
struct Result { std::wstring failure; bool cancelled = false; };
struct View {
    Options options;
    std::function<void(const Options&)> execute;
    bool uninstall = false, busy = false, succeeded = false, cancelAccepted = false;
    int result = 1;
    HWND host = nullptr;
    std::wstring caption;
    std::array<std::wstring, 4> titles, subtitles;
};
static void Label(HWND page, int id, const std::string& key) { SetDlgItemTextW(page, id, Text(key).c_str()); }
static void SavePath(HWND page, View& view, bool validate = true) {
    wchar_t path[32768]{};
    GetDlgItemTextW(page, 1011, path, 32768);
    view.options.root = validate ? Destination(path) : std::wstring(path);
    view.options.desktop = IsDlgButtonChecked(page, 1014) == BST_CHECKED;
    view.options.menu = true;
    SetDlgItemTextW(page, 1011, view.options.root.c_str());
}
static void Browse(HWND page, View& view) {
    IFileOpenDialog* dialog = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog)))) return;
    std::unique_ptr<IFileOpenDialog, void(*)(IFileOpenDialog*)> scoped(dialog, [](auto value) { value->Release(); });
    DWORD flags = 0;
    if (SUCCEEDED(dialog->GetOptions(&flags))) dialog->SetOptions(flags | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR);
    dialog->SetTitle(Text("Installer_Directory").c_str());
    if (FAILED(dialog->Show(page))) return;
    IShellItem* item = nullptr;
    if (FAILED(dialog->GetResult(&item))) return;
    std::unique_ptr<IShellItem, void(*)(IShellItem*)> folder(item, [](auto value) { value->Release(); });
    PWSTR path = nullptr;
    if (SUCCEEDED(folder->GetDisplayName(SIGDN_FILESYSPATH, &path))) {
        std::wstring selected(path);
        CoTaskMemFree(path);
        view.options.root = Destination(selected);
        SetDlgItemTextW(page, 1011, view.options.root.c_str());
    }
}
static std::wstring Overview(const View& view) {
    if (view.uninstall) return Text("Installer_Directory") + L"\r\n" + view.options.root + L"\r\n\r\n" + Text("Installer_RetainData");
    if (view.options.extract) return Text("Installer_ExtractDirectory") + L"\r\n" + view.options.root + L"\r\n\r\n" + Text("Installer_ExtractOnly");
    return Text("Installer_Directory") + L"\r\n" + view.options.root + L"\r\n\r\n" + Text("Installer_Version") + L": " + EGL_VERSION + L"\r\n\r\n" + Text("Installer_StartMenu") + L": " + Text("Installer_Required") + L"\r\n" + Text("Installer_Desktop") + L": " + Text(view.options.desktop ? "Installer_Selected" : "Installer_NotSelected");
}
static void Buttons(HWND dialog, View& view, int page) {
    DWORD buttons = page == 0 ? PSWIZB_NEXT : page == 1 ? PSWIZB_BACK : page == 2 ? PSWIZB_BACK | PSWIZB_NEXT : PSWIZB_FINISH;
    if (view.busy) buttons = 0;
    PropSheet_SetWizButtons(view.host, buttons);
    PostMessageW(dialog, WM_APP + 2, static_cast<WPARAM>(page), 0);
}
static void ButtonLabels(View& view, int page) {
    Label(view.host, WizardBack, "Installer_Back");
    Label(view.host, WizardNext, page == 2 ? view.uninstall ? "Installer_Uninstall" : view.options.extract ? "Installer_BeginExtract" : "Installer_BeginInstall" : "Installer_Next");
    Label(view.host, WizardFinish, "Installer_Close");
    Label(view.host, IDCANCEL, "Installer_Cancel");
    ShowWindow(GetDlgItem(view.host, WizardNext), page == 1 || page == 3 ? SW_HIDE : SW_SHOW);
    ShowWindow(GetDlgItem(view.host, WizardBack), page == 0 || page == 3 ? SW_HIDE : SW_SHOW);
    ShowWindow(GetDlgItem(view.host, IDCANCEL), page == 3 ? SW_HIDE : SW_SHOW);
    EnableWindow(GetDlgItem(view.host, IDCANCEL), !view.cancelAccepted);
}
static void Start(HWND page, View& view) {
    view.options.cancellation = std::make_shared<std::atomic_bool>(false);
    wchar_t temporary[32768]{};
    Check(GetTempPathW(32768, temporary) > 0, "GetTempPath");
    auto folder = fs::path(temporary) / L"eric" / L"ericgamelauncher" / L"setup";
    fs::create_directories(folder);
    view.options.cancelFile = (folder / (L"cancel-" + Guid())).wstring();
    view.busy = true;
    Buttons(page, view, 2);
    Label(page, 1021, "Installer_Working");
    std::thread([page, execute = view.execute, options = view.options] {
        auto result = std::make_unique<Result>();
        HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        try { Check(SUCCEEDED(initialized), "CoInitializeEx"); execute(options); }
        catch (const std::exception& error) { result->failure = Wide(error.what()); result->cancelled = result->failure == L"INSTALL_CANCELLED"; Log(result->failure); }
        if (SUCCEEDED(initialized)) CoUninitialize();
        if (PostMessageW(page, WM_APP + 1, 0, reinterpret_cast<LPARAM>(result.get()))) result.release();
    }).detach();
}
static INT_PTR CALLBACK Page(HWND page, UINT message, WPARAM wParam, LPARAM lParam) {
    auto descriptor = reinterpret_cast<PROPSHEETPAGEW*>(GetWindowLongPtrW(page, GWLP_USERDATA));
    if (message == WM_INITDIALOG) {
        descriptor = reinterpret_cast<PROPSHEETPAGEW*>(lParam);
        SetWindowLongPtrW(page, GWLP_USERDATA, lParam);
    }
    if (!descriptor) return FALSE;
    auto& view = *reinterpret_cast<View*>(descriptor->lParam);
    int index = static_cast<int>(reinterpret_cast<ULONG_PTR>(descriptor->pszTemplate)) - 200;
    view.host = GetParent(page);
    try {
        if (message == WM_INITDIALOG) {
            if (index == 0) {
                Label(page, 1000, view.uninstall ? "Installer_UninstallTitle" : "Installer_Welcome");
                Label(page, 1001, view.uninstall ? "Installer_RetainData" : "Installer_WelcomeBody");
                SetDlgItemTextW(page, 1002, (Text("Installer_Version") + L": " + EGL_VERSION).c_str());
            } else if (index == 1) {
                Label(page, 1010, "Installer_TargetDirectory");
                Label(page, 1012, "Installer_Browse");
                Label(page, 1013, "Installer_StartMenu");
                Label(page, 1014, "Installer_Desktop");
                Label(page, 1015, "Installer_ExtractNote");
                Label(page, 1016, "Installer_Install");
                Label(page, 1017, "Installer_Extract");
                CheckDlgButton(page, 1013, BST_CHECKED);
                EnableWindow(GetDlgItem(page, 1013), FALSE);
            }
            return TRUE;
        }
        if (message == WM_COMMAND) {
            if (index == 1) {
                if (LOWORD(wParam) == 1012 && HIWORD(wParam) == BN_CLICKED) { Browse(page, view); return TRUE; }
                if ((LOWORD(wParam) == 1016 || LOWORD(wParam) == 1017) && HIWORD(wParam) == BN_CLICKED) {
                    SavePath(page, view);
                    view.options.extract = LOWORD(wParam) == 1017;
                    PropSheet_SetCurSelByID(view.host, 202);
                    return TRUE;
                }
                if (LOWORD(wParam) == 1011 && HIWORD(wParam) == EN_KILLFOCUS) {
                    try { SavePath(page, view); } catch (const std::exception& error) { Log(Wide(error.what())); }
                    return TRUE;
                }
            }
            if (index == 3 && LOWORD(wParam) == 1033) { ShellExecuteW(page, L"open", view.options.root.c_str(), nullptr, nullptr, SW_SHOWNORMAL); return TRUE; }
            if (index == 3 && LOWORD(wParam) == 1034) {
                auto executable = fs::path(view.options.root) / L"EricGameLauncher.exe";
                ShellExecuteW(page, L"open", executable.c_str(), nullptr, view.options.root.c_str(), SW_SHOWNORMAL);
                return TRUE;
            }
        }
        if (message == WM_NOTIFY) {
            auto notification = reinterpret_cast<NMHDR*>(lParam);
            switch (notification->code) {
                case PSN_SETACTIVE:
                    if (index == 1) {
                        SetDlgItemTextW(page, 1011, view.options.root.c_str());
                        CheckDlgButton(page, 1014, view.options.desktop ? BST_CHECKED : BST_UNCHECKED);
                    } else if (index == 2) {
                        SetDlgItemTextW(page, 1020, Overview(view).c_str());
                        Label(page, 1021, view.busy ? "Installer_Working" : "Installer_Ready");
                    } else if (index == 3) {
                        Label(page, 1030, view.succeeded ? "Installer_Complete" : "Installer_FailedTitle");
                        Label(page, 1031, view.succeeded ? view.uninstall ? "Installer_Uninstalled" : view.options.extract ? "Installer_Extracted" : "Installer_Installed" : "Installer_Failed");
                        SetDlgItemTextW(page, 1032, view.options.root.c_str());
                        Label(page, 1033, "Installer_OpenDirectory");
                        Label(page, 1034, "Installer_OpenApplication");
                        ShowWindow(GetDlgItem(page, 1033), view.succeeded ? SW_SHOW : SW_HIDE);
                        ShowWindow(GetDlgItem(page, 1034), view.succeeded && !view.uninstall ? SW_SHOW : SW_HIDE);
                        EnableWindow(GetDlgItem(page, 1033), fs::exists(view.options.root));
                        EnableWindow(GetDlgItem(page, 1034), fs::is_regular_file(fs::path(view.options.root) / L"EricGameLauncher.exe"));
                    }
                    Buttons(page, view, index);
                    SetWindowLongPtrW(page, DWLP_MSGRESULT, 0);
                    Log(L"Standard wizard page=" + std::to_wstring(index));
                    return TRUE;
                case PSN_WIZNEXT:
                    if (index == 0 && view.uninstall) SetWindowLongPtrW(page, DWLP_MSGRESULT, 202);
                    else if (index == 2) { SetWindowLongPtrW(page, DWLP_MSGRESULT, -1); if (!view.busy) Start(page, view); }
                    else SetWindowLongPtrW(page, DWLP_MSGRESULT, 0);
                    return TRUE;
                case PSN_WIZBACK:
                    if (view.busy) SetWindowLongPtrW(page, DWLP_MSGRESULT, -1);
                    else {
                        if (index == 1) SavePath(page, view, false);
                        SetWindowLongPtrW(page, DWLP_MSGRESULT, view.uninstall ? 200 : 0);
                    }
                    return TRUE;
                case PSN_QUERYCANCEL:
                    if (!view.cancelAccepted && index != 0 && index != 3 && MessageBoxW(page, Text("Installer_CancelConfirm").c_str(), view.caption.c_str(), MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) { SetWindowLongPtrW(page, DWLP_MSGRESULT, TRUE); return TRUE; }
                    view.cancelAccepted = true;
                    if (view.busy) {
                        view.options.cancellation->store(true);
                        Handle signal(CreateFileW(view.options.cancelFile.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr));
                        EnableWindow(GetDlgItem(view.host, IDCANCEL), FALSE);
                        Label(page, 1021, "Installer_Cancelling");
                    }
                    SetWindowLongPtrW(page, DWLP_MSGRESULT, view.busy ? TRUE : FALSE);
                    return TRUE;
            }
        }
        if (message == WM_APP + 1) {
            std::unique_ptr<Result> result(reinterpret_cast<Result*>(lParam));
            view.busy = false;
            DeleteFileW(view.options.cancelFile.c_str());
            if (result->cancelled) { EnableWindow(GetDlgItem(view.host, IDCANCEL), TRUE); PropSheet_PressButton(view.host, PSBTN_CANCEL); return TRUE; }
            view.succeeded = result->failure.empty();
            view.result = view.succeeded ? 0 : 1;
            PropSheet_SetCurSelByID(view.host, 203);
            return TRUE;
        }
        if (message == WM_APP + 2) { ButtonLabels(view, static_cast<int>(wParam)); return TRUE; }
    } catch (const std::exception& error) {
        Log(Wide(error.what()));
        if (message == WM_NOTIFY) SetWindowLongPtrW(page, DWLP_MSGRESULT, -1);
        MessageBoxW(page, Text(index == 1 ? "Installer_InvalidDirectory" : "Installer_Failed").c_str(), view.caption.c_str(), MB_OK | MB_ICONERROR);
        return TRUE;
    }
    return FALSE;
}
int Window(Options options, const std::function<void(const Options&)>& execute, bool uninstall) {
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_WIN95_CLASSES };
    Check(InitCommonControlsEx(&controls) != 0, "InitCommonControlsEx");
    View view;
    view.options = options; view.execute = execute; view.uninstall = uninstall;
    view.caption = Text(uninstall ? "Installer_UninstallTitle" : "Installer_Title");
    view.titles = {Text("Installer_Welcome"), Text("Installer_Choose"), Text(uninstall ? "Installer_UninstallTitle" : "Installer_Overview"), Text("Installer_Complete")};
    view.subtitles = {L"", Text("Installer_TargetDirectory"), Text("Installer_ReviewSubtitle"), L""};
    std::array<PROPSHEETPAGEW, 4> pages{};
    for (size_t i = 0; i < pages.size(); ++i) {
        pages[i].dwSize = sizeof(PROPSHEETPAGEW);
        pages[i].dwFlags = i == 0 || i == 3 ? PSP_HIDEHEADER : PSP_USEHEADERTITLE | PSP_USEHEADERSUBTITLE;
        pages[i].hInstance = GetModuleHandleW(nullptr);
        pages[i].pszTemplate = MAKEINTRESOURCEW(200 + i);
        pages[i].pfnDlgProc = Page;
        pages[i].lParam = reinterpret_cast<LPARAM>(&view);
        pages[i].pszHeaderTitle = view.titles[i].c_str();
        pages[i].pszHeaderSubTitle = view.subtitles[i].c_str();
    }
    if (uninstall) { pages[1] = pages[2]; pages[2] = pages[3]; }
    PROPSHEETHEADERW header{};
    header.dwSize = sizeof(header);
    header.dwFlags = PSH_WIZARD97 | PSH_PROPSHEETPAGE | PSH_USEICONID;
    header.hInstance = GetModuleHandleW(nullptr);
    header.pszIcon = MAKEINTRESOURCEW(100);
    header.pszCaption = view.caption.c_str();
    header.nPages = uninstall ? 3 : 4;
    header.ppsp = pages.data();
    Check(PropertySheetW(&header) != -1, "PropertySheet");
    return view.result;
}
}
