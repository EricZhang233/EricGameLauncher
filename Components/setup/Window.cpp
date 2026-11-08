#include "../setup.core/Installation.h"
#include "State.h"
#include <commctrl.h>
#include <dwmapi.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <uxtheme.h>
#include <thread>

namespace setup {
enum ControlId { Action = 1, Cancel = 2, Back = 3, Extract = 4, BrowseDirectory = 5, OpenDirectory = 6, OpenApplication = 7, Directory = 8, Desktop = 9, Status = 10, KeepData = 11 };
struct Result { std::wstring failure; bool cancelled = false; };
struct Palette {
    COLORREF background = RGB(243, 243, 243), text = RGB(32, 32, 32), muted = RGB(96, 96, 96);
    COLORREF surface = RGB(255, 255, 255), border = RGB(210, 210, 210), accent = RGB(0, 95, 184);
    COLORREF error = RGB(196, 43, 28), success = RGB(16, 124, 16);
    bool contrast = false;
};
struct View {
    Options options;
    Options choices;
    Record installed;
    std::function<void(const Options&)> execute;
    OperationState operation;
    Palette colors;
    bool uninstall = false, cancelAccepted = false, animations = true;
    static constexpr int height = 300;
    int page = 0, result = 1, dpi = 96, defaultButton = Action;
    HFONT font = nullptr, titleFont = nullptr, metadataFont = nullptr, emphasisFont = nullptr;
    HICON icon = nullptr;
    HBRUSH background = nullptr, surface = nullptr;
    HWND host = nullptr, directory = nullptr, desktop = nullptr, status = nullptr, progress = nullptr, hot = nullptr;
    std::vector<HWND> controls;
};
static int Scale(const View& view, int value) { return MulDiv(value, view.dpi, 96); }
static COLORREF Blend(COLORREF first, COLORREF second, int percentage) {
    return RGB((GetRValue(first) * (100 - percentage) + GetRValue(second) * percentage) / 100,
        (GetGValue(first) * (100 - percentage) + GetGValue(second) * percentage) / 100,
        (GetBValue(first) * (100 - percentage) + GetBValue(second) * percentage) / 100);
}
static void Appearance(View& view) {
    view.colors = Palette{};
    HIGHCONTRASTW contrast{ sizeof(contrast) };
    if (SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0) && (contrast.dwFlags & HCF_HIGHCONTRASTON)) {
        view.colors.contrast = true;
        view.colors.background = view.colors.surface = GetSysColor(COLOR_WINDOW);
        view.colors.text = GetSysColor(COLOR_WINDOWTEXT);
        view.colors.muted = GetSysColor(COLOR_GRAYTEXT);
        view.colors.border = GetSysColor(COLOR_WINDOWTEXT);
        view.colors.accent = GetSysColor(COLOR_HIGHLIGHT);
        view.colors.success = view.colors.error = view.colors.text;
    } else {
        DWORD color = 0; BOOL opaque = FALSE;
        if (SUCCEEDED(DwmGetColorizationColor(&color, &opaque))) {
            COLORREF accent = RGB((color >> 16) & 255, (color >> 8) & 255, color & 255);
            int lightness = GetRValue(accent) * 299 + GetGValue(accent) * 587 + GetBValue(accent) * 114;
            view.colors.accent = lightness > 150000 ? Blend(accent, RGB(0, 0, 0), 28) : accent;
        }
    }
    BOOL animations = TRUE;
    SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &animations, 0);
    view.animations = animations != FALSE;
    if (view.background) DeleteObject(view.background);
    if (view.surface) DeleteObject(view.surface);
    view.background = CreateSolidBrush(view.colors.background);
    view.surface = CreateSolidBrush(view.colors.surface);
    if (view.host && !view.colors.contrast) {
        DWM_WINDOW_CORNER_PREFERENCE corners = DWMWCP_ROUND;
        DwmSetWindowAttribute(view.host, DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(corners));
        DwmSetWindowAttribute(view.host, DWMWA_CAPTION_COLOR, &view.colors.background, sizeof(COLORREF));
        DwmSetWindowAttribute(view.host, DWMWA_TEXT_COLOR, &view.colors.text, sizeof(COLORREF));
    }
}
static void Fonts(View& view) {
    for (HFONT font : {view.font, view.titleFont, view.metadataFont, view.emphasisFont}) if (font) DeleteObject(font);
    auto family = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_CHINESE ? L"Microsoft YaHei UI" : L"Segoe UI";
    view.font = CreateFontW(-Scale(view, 15), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, family);
    view.titleFont = CreateFontW(-Scale(view, 22), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, family);
    view.metadataFont = CreateFontW(-Scale(view, 13), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, family);
    view.emphasisFont = CreateFontW(-Scale(view, 15), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, family);
    if (view.icon) DestroyIcon(view.icon);
    view.icon = static_cast<HICON>(LoadImageW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(100), IMAGE_ICON, Scale(view, 56), Scale(view, 56), 0));
}
static LRESULT CALLBACK ButtonProcedure(HWND button, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR data) {
    auto& view = *reinterpret_cast<View*>(data);
    if (message == WM_MOUSEMOVE && view.hot != button) {
        if (view.hot) InvalidateRect(view.hot, nullptr, FALSE);
        view.hot = button;
        TRACKMOUSEEVENT tracking{ sizeof(tracking), TME_LEAVE, button, 0 };
        TrackMouseEvent(&tracking);
        InvalidateRect(button, nullptr, FALSE);
    }
    if (message == WM_MOUSELEAVE || message == WM_NCDESTROY) {
        if (view.hot == button) view.hot = nullptr;
        if (message == WM_MOUSELEAVE) InvalidateRect(button, nullptr, FALSE);
    }
    if (message == WM_SETFOCUS || message == WM_KILLFOCUS) InvalidateRect(button, nullptr, FALSE);
    return DefSubclassProc(button, message, wParam, lParam);
}
static HWND Control(View& view, const wchar_t* type, const std::wstring& text, DWORD style, int x, int y, int width, int height, int id = 0, HFONT font = nullptr, bool secondary = false) {
    if (wcscmp(type, L"STATIC") == 0) style |= SS_NOPREFIX;
    HWND control = CreateWindowExW(0, type, text.c_str(), WS_CHILD | WS_VISIBLE | style, Scale(view, x), Scale(view, y), Scale(view, width), Scale(view, height), view.host, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), GetModuleHandleW(nullptr), nullptr);
    Check(control != nullptr, "CreateControl");
    SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(font ? font : view.font), TRUE);
    if (secondary) Check(SetPropW(control, L"EGL.SecondaryText", reinterpret_cast<HANDLE>(1)) != FALSE, "SetTextStyle");
    if (!view.colors.contrast) SetWindowTheme(control, L"Explorer", nullptr);
    if (wcscmp(type, L"BUTTON") == 0 && (style & BS_TYPEMASK) == BS_OWNERDRAW)
        SetWindowSubclass(control, ButtonProcedure, 1, reinterpret_cast<DWORD_PTR>(&view));
    view.controls.push_back(control);
    return control;
}
static HWND Button(View& view, const std::string& key, int x, int width, int id) {
    return Control(view, L"BUTTON", Text(key), WS_TABSTOP | BS_OWNERDRAW, x, view.height - 52, width, 32, id);
}
static void SavePath(View& view, bool validate) {
    if (!view.directory) return;
    wchar_t path[32768]{};
    GetWindowTextW(view.directory, path, 32768);
    view.options.root = validate ? Destination(path) : std::wstring(path);
    if (view.desktop) view.options.desktop = SendMessageW(view.desktop, BM_GETCHECK, 0, 0) == BST_CHECKED;
    view.options.menu = true;
    view.choices.root = view.options.root;
    if (!view.options.extract) view.choices.desktop = view.options.desktop;
    if (validate) SetWindowTextW(view.directory, view.options.root.c_str());
}
static void Browse(View& view) {
    IFileOpenDialog* dialog = nullptr;
    Check(SUCCEEDED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog))), "FolderDialog");
    std::unique_ptr<IFileOpenDialog, void(*)(IFileOpenDialog*)> scoped(dialog, [](auto value) { value->Release(); });
    DWORD flags = 0;
    Check(SUCCEEDED(dialog->GetOptions(&flags)) && SUCCEEDED(dialog->SetOptions(flags | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR)), "FolderOptions");
    dialog->SetTitle(Text("Installer_TargetDirectory").c_str());
    if (FAILED(dialog->Show(view.host))) return;
    IShellItem* item = nullptr;
    Check(SUCCEEDED(dialog->GetResult(&item)), "FolderResult");
    std::unique_ptr<IShellItem, void(*)(IShellItem*)> folder(item, [](auto value) { value->Release(); });
    PWSTR path = nullptr;
    Check(SUCCEEDED(folder->GetDisplayName(SIGDN_FILESYSPATH, &path)), "FolderPath");
    std::wstring selected(path);
    CoTaskMemFree(path);
    view.options.root = Destination(selected);
    SetWindowTextW(view.directory, view.options.root.c_str());
}
static std::string WorkingKey(const View& view) {
    return view.uninstall ? "Installer_Uninstalling" : view.options.extract ? "Installer_Extracting" : view.options.update ? "Installer_Upgrading" : "Installer_Installing";
}
static std::string StatusKey(const View& view) {
    switch (view.operation.phase) {
        case OperationPhase::Ready: return "Installer_Ready";
        case OperationPhase::Running: return WorkingKey(view);
        case OperationPhase::Cancelling: return "Installer_Cancelling";
        case OperationPhase::Succeeded: return view.uninstall ? view.options.keepData ? "Installer_UninstalledRetained" : "Installer_Uninstalled" : view.options.extract ? "Installer_Extracted" : view.options.update ? "Installer_Upgraded" : "Installer_Installed";
        case OperationPhase::Failed: return "Installer_Failed";
        case OperationPhase::Cancelled: return "Installer_Cancelled";
    }
    return "Installer_Ready";
}
enum class TextStyle { Normal, Emphasis, Secondary };
struct TextBlock { std::wstring text; TextStyle style = TextStyle::Normal; int gap = 0; };
static std::vector<TextBlock> OverviewBlocks(const View& view) {
    if (view.uninstall) return {
        {Text("Installer_Directory") + L": " + view.options.root},
        {Text("Installer_DataDirectory") + L": " + UninstallDataDirectory(view.options).wstring()},
        {Text("Installer_UninstallDataDefault"), TextStyle::Secondary, 4}};
    if (view.options.update && !view.options.extract) return {{UpgradeSummary(view.installed, EGL_VERSION), TextStyle::Emphasis}};
    std::vector<TextBlock> blocks{
        {Text(view.options.extract ? "Installer_ExtractDirectory" : "Installer_Directory"), TextStyle::Emphasis},
        {view.options.root}};
    if (view.options.extract) blocks.push_back({Text("Installer_ExtractOnly"), TextStyle::Secondary, 8});
    else if (view.options.desktop) blocks.push_back({Text("Installer_WillCreateDesktop"), TextStyle::Secondary, 8});
    return blocks;
}
static std::wstring Overview(const View& view) {
    std::wstring text;
    for (const auto& block : OverviewBlocks(view)) {
        if (!text.empty()) text += block.gap ? L"\r\n\r\n" : L"\r\n";
        text += block.text;
    }
    return text;
}
static std::wstring HomeText(const View& view) {
    if (view.uninstall) return Text("Installer_UninstallDescription");
    if (!view.installed) return Text("Installer_WelcomeBody");
    return Text("Installer_RegisteredBody") + L"\r\n" + Text("Installer_InstalledVersion") + L": "
        + (view.installed.version.empty() ? Text("Installer_UnknownVersion") : view.installed.version);
}
static int TextHeight(View& view, const std::wstring& text, HFONT font = nullptr) {
    HDC context = GetDC(view.host);
    HGDIOBJ previous = SelectObject(context, font ? font : view.font);
    RECT rectangle{0, 0, Scale(view, 520), 0};
    DrawTextW(context, text.c_str(), static_cast<int>(text.size()), &rectangle, DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX | DT_EDITCONTROL);
    SelectObject(context, previous);
    ReleaseDC(view.host, context);
    return std::max(20, MulDiv(rectangle.bottom, 96, view.dpi) + 4);
}
static HFONT BlockFont(const View& view, const TextBlock& block) {
    return block.style == TextStyle::Emphasis ? view.emphasisFont : block.style == TextStyle::Secondary ? view.metadataFont : view.font;
}
static int BlocksHeight(View& view, const std::vector<TextBlock>& blocks) {
    int height = 0;
    for (const auto& block : blocks) height += block.gap + TextHeight(view, block.text, BlockFont(view, block));
    return height;
}
static void RenderBlocks(View& view, const std::vector<TextBlock>& blocks, int top) {
    for (const auto& block : blocks) {
        top += block.gap;
        int height = TextHeight(view, block.text, BlockFont(view, block));
        Control(view, L"STATIC", block.text, 0, 20, top, 520, height, 0, BlockFont(view, block), block.style == TextStyle::Secondary);
        top += height;
    }
}
static void ClearControls(View& view) {
    for (HWND control : view.controls) DestroyWindow(control);
    view.controls.clear();
    view.directory = view.desktop = view.status = view.progress = view.hot = nullptr;
}
static void Render(View& view) {
    ClearControls(view);
    if (view.page == 0 && !view.uninstall) view.installed = RegisteredInstallation(view.options.owner);
    bool upgradeConfirmation = view.page == 2 && view.options.update && !view.options.extract && !view.uninstall;
    int contentTop = upgradeConfirmation ? 76 : 86;
    auto body = view.page == 0 ? HomeText(view) : Overview(view);
    auto blocks = OverviewBlocks(view);
    int measuredBodyHeight = view.page == 0 ? TextHeight(view, body) : BlocksHeight(view, blocks);
    int bodyHeight = std::min(104, measuredBodyHeight);
    bool dataChoice = view.uninstall && view.page == 2 && !view.operation.Pending();
    int messageHeight = std::min(64, TextHeight(view, Text(StatusKey(view)), view.emphasisFont));
    int pathHeight = std::min(64, TextHeight(view, view.options.root, view.metadataFont));
    int statusTop = contentTop + bodyHeight + 12;
    int pathTop = 86 + messageHeight + 12;
    SetWindowTextW(view.host, Text(view.uninstall ? "Installer_UninstallWindowTitle" : "Installer_WindowTitle").c_str());
    std::string heading = view.page == 0 ? view.uninstall ? "Installer_UninstallTitle" : view.installed ? "Installer_UpgradeTitle" : "Installer_Title"
        : view.page == 1 ? "Installer_Choose" : view.page == 2 ? "Installer_Overview"
        : view.operation.phase == OperationPhase::Succeeded ? "Installer_Complete" : "Installer_FailedTitle";
    Control(view, L"STATIC", Text(heading), 0, 20, 14, 444, 32, 0, view.titleFont);
    if (!upgradeConfirmation) Control(view, L"STATIC", Text("Installer_Publisher") + L": EricSoft  ·  " + Text("Installer_Version") + L": " + EGL_VERSION, 0, 20, 50, 444, 20, 0, view.metadataFont, true);
    if (view.page == 0) {
        Control(view, L"STATIC", HomeText(view), 0, 20, contentTop, 520, bodyHeight);
        Button(view, view.uninstall ? "Installer_Next" : view.installed ? "Installer_Upgrade" : "Installer_Install", view.uninstall ? 332 : 224, 100, Action);
        if (!view.uninstall) Button(view, "Installer_Extract", 332, 100, Extract);
    } else if (view.page == 1) {
        Control(view, L"STATIC", Text(view.options.extract ? "Installer_ExtractDirectory" : "Installer_Directory"), 0, 20, 86, 520, 20, 0, view.emphasisFont);
        view.directory = Control(view, L"EDIT", view.options.root, WS_BORDER | WS_TABSTOP | ES_AUTOHSCROLL, 20, 110, 412, 30, Directory);
        SendMessageW(view.directory, EM_SETLIMITTEXT, 32767, 0);
        Control(view, L"BUTTON", Text("Installer_Browse"), WS_TABSTOP | BS_OWNERDRAW, 444, 109, 96, 32, BrowseDirectory);
        if (view.options.extract) Control(view, L"STATIC", Text("Installer_ExtractOnly"), 0, 20, 154, 520, TextHeight(view, Text("Installer_ExtractOnly"), view.metadataFont), 0, view.metadataFont, true);
        else {
            HWND menu = Control(view, L"BUTTON", Text("Installer_StartMenu"), BS_AUTOCHECKBOX, 20, 152, 264, 24);
            SendMessageW(menu, BM_SETCHECK, BST_CHECKED, 0);
            EnableWindow(menu, FALSE);
            view.desktop = Control(view, L"BUTTON", Text("Installer_Desktop"), WS_TABSTOP | BS_AUTOCHECKBOX, 292, 152, 248, 24, Desktop);
            SendMessageW(view.desktop, BM_SETCHECK, view.options.desktop ? BST_CHECKED : BST_UNCHECKED, 0);
            Control(view, L"STATIC", Text("Installer_Description"), 0, 20, 188, 520, TextHeight(view, Text("Installer_Description"), view.metadataFont), 0, view.metadataFont, true);
        }
        Button(view, "Installer_Back", 20, 100, Back);
        Button(view, "Installer_Next", 332, 100, Action);
    } else if (view.page == 2) {
        if (measuredBodyHeight > bodyHeight)
            Control(view, L"EDIT", body, ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL | WS_TABSTOP, 20, contentTop, 520, bodyHeight);
        else RenderBlocks(view, blocks, contentTop);
        if (dataChoice) {
            HWND keep = Control(view, L"BUTTON", Text("Installer_RetainData"), WS_TABSTOP | BS_AUTOCHECKBOX, 20, statusTop, 520, 24, KeepData, view.emphasisFont);
            SendMessageW(keep, BM_SETCHECK, view.options.keepData ? BST_CHECKED : BST_UNCHECKED, 0);
        }
        HWND back = Button(view, "Installer_Back", 20, 100, Back);
        HWND action = Button(view, view.uninstall ? "Installer_Uninstall" : view.options.extract ? "Installer_BeginExtract" : view.options.update ? "Installer_BeginUpgrade" : "Installer_BeginInstall", 332, 100, Action);
        EnableWindow(back, !view.operation.Pending());
        EnableWindow(action, !view.operation.Pending());
    } else {
        Control(view, L"STATIC", Text(StatusKey(view)), 0, 20, 86, 520, messageHeight, Status, view.emphasisFont);
        if (view.uninstall && view.options.keepData && view.operation.phase == OperationPhase::Succeeded) {
            auto dataPath = UninstallDataDirectory(view.options).wstring();
            Control(view, L"STATIC", Text("Installer_RetainedDataLocation"), 0, 20, pathTop, 520, 20, 0, view.metadataFont, true);
            Control(view, L"STATIC", dataPath, 0, 20, pathTop + 24, 520, std::min(64, TextHeight(view, dataPath)));
        } else Control(view, L"STATIC", view.options.root, 0, 20, pathTop, 520, pathHeight, 0, view.metadataFont, true);
        if (!view.uninstall && view.operation.phase == OperationPhase::Succeeded) {
            HWND path = Button(view, "Installer_OpenDirectory", view.options.extract ? 332 : 192, view.options.extract ? 100 : 116, OpenDirectory);
            EnableWindow(path, fs::is_directory(view.options.root));
            if (!view.options.extract) {
                HWND app = Button(view, "Installer_OpenApplication", 316, 116, OpenApplication);
                EnableWindow(app, fs::is_regular_file(fs::path(view.options.root) / L"EricGameLauncher.exe"));
            }
        }
    }
    if (view.operation.Pending()) {
        view.status = Control(view, L"STATIC", Text(StatusKey(view)), SS_LEFT, 20, statusTop, 520, 24, Status, view.emphasisFont);
        view.progress = Control(view, PROGRESS_CLASSW, L"", PBS_SMOOTH | (view.animations ? PBS_MARQUEE : 0), 20, statusTop + 30, 520, 5);
        SendMessageW(view.progress, PBM_SETRANGE32, 0, 100);
        if (view.animations) SendMessageW(view.progress, PBM_SETMARQUEE, TRUE, 28);
        else SendMessageW(view.progress, PBM_SETPOS, 0, 0);
    }
    HWND cancel = Button(view, view.page == 3 ? "Installer_Close" : "Installer_Cancel", 440, 100, Cancel);
    EnableWindow(cancel, !view.cancelAccepted || view.page == 3);
    view.defaultButton = view.page == 3 ? Cancel : view.page == 0 && view.options.extract && !view.uninstall ? Extract : Action;
    if (!view.operation.Pending()) SetFocus(GetDlgItem(view.host, view.defaultButton));
    InvalidateRect(view.host, nullptr, TRUE);
    Log(L"Modern installer page=" + std::to_wstring(view.page) + L" phase=" + std::to_wstring(static_cast<int>(view.operation.phase)));
}
static void DrawButton(View& view, const DRAWITEMSTRUCT& item) {
    bool enabled = (item.itemState & ODS_DISABLED) == 0;
    UINT primaryAction = view.page == 0 && view.options.extract && !view.uninstall ? Extract : Action;
    bool primary = item.CtlID == primaryAction || item.CtlID == OpenApplication || (view.page == 3 && item.CtlID == Cancel);
    COLORREF background = primary ? view.colors.accent : view.colors.surface;
    COLORREF foreground = primary ? view.colors.contrast ? GetSysColor(COLOR_HIGHLIGHTTEXT) : RGB(255, 255, 255) : view.colors.text;
    if (!enabled) { background = Blend(view.colors.background, view.colors.border, 28); foreground = view.colors.muted; }
    else if (item.itemState & ODS_SELECTED) background = Blend(background, view.colors.text, 12);
    else if (view.hot == item.hwndItem) background = Blend(background, primary ? RGB(255, 255, 255) : view.colors.accent, primary ? 9 : 5);
    HBRUSH fill = CreateSolidBrush(background);
    FillRect(item.hDC, &item.rcItem, view.background);
    HPEN outline = CreatePen(PS_SOLID, Scale(view, 1), primary ? background : view.colors.border);
    HGDIOBJ oldBrush = SelectObject(item.hDC, fill), oldPen = SelectObject(item.hDC, outline);
    int radius = Scale(view, view.colors.contrast ? 0 : 8);
    RoundRect(item.hDC, item.rcItem.left, item.rcItem.top, item.rcItem.right, item.rcItem.bottom, radius, radius);
    SetBkMode(item.hDC, TRANSPARENT);
    SetTextColor(item.hDC, foreground);
    HGDIOBJ oldFont = SelectObject(item.hDC, primary ? view.emphasisFont : view.font);
    wchar_t text[512]{};
    GetWindowTextW(item.hwndItem, text, 512);
    RECT label = item.rcItem;
    DrawTextW(item.hDC, text, -1, &label, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_HIDEPREFIX);
    if (enabled && (item.itemState & ODS_FOCUS)) { RECT focus = item.rcItem; InflateRect(&focus, -Scale(view, 4), -Scale(view, 4)); DrawFocusRect(item.hDC, &focus); }
    SelectObject(item.hDC, oldFont); SelectObject(item.hDC, oldBrush); SelectObject(item.hDC, oldPen);
    DeleteObject(fill); DeleteObject(outline);
}
static void Paint(View& view) {
    PAINTSTRUCT paint{};
    HDC output = BeginPaint(view.host, &paint);
    RECT client{}; GetClientRect(view.host, &client);
    HDC memory = CreateCompatibleDC(output);
    HBITMAP bitmap = CreateCompatibleBitmap(output, client.right, client.bottom);
    HGDIOBJ old = SelectObject(memory, bitmap);
    FillRect(memory, &client, view.background);
    if (view.icon) DrawIconEx(memory, Scale(view, 484), Scale(view, 16), view.icon, Scale(view, 56), Scale(view, 56), 0, nullptr, DI_NORMAL);
    BitBlt(output, 0, 0, client.right, client.bottom, memory, 0, 0, SRCCOPY);
    SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory);
    EndPaint(view.host, &paint);
}
static void Begin(View& view) {
    view.options.cancellation = std::make_shared<std::atomic_bool>(false);
    wchar_t temporary[32768]{};
    Check(GetTempPathW(32768, temporary) > 0, "GetTempPath");
    fs::path folder = fs::path(temporary) / L"eric" / L"ericgamelauncher" / L"setup";
    fs::create_directories(folder);
    view.options.cancelFile = (folder / (L"cancel-" + Guid())).wstring();
    view.operation.Begin();
    Render(view);
    std::thread([window = view.host, execute = view.execute, options = view.options] {
        auto result = std::make_unique<Result>();
        HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        try { Check(SUCCEEDED(initialized), "CoInitializeEx"); execute(options); }
        catch (const std::exception& error) { result->failure = Wide(error.what()); result->cancelled = result->failure == L"INSTALL_CANCELLED"; Log(result->failure); }
        catch (...) { result->failure = L"UNEXPECTED_OPERATION_FAILURE"; Log(result->failure); }
        if (SUCCEEDED(initialized)) CoUninitialize();
        if (PostMessageW(window, WM_APP + 1, 0, reinterpret_cast<LPARAM>(result.get()))) result.release();
    }).detach();
}
static void CancelOperation(View& view) {
    if (view.cancelAccepted && view.operation.Pending()) return;
    if (view.page != 0 && view.page != 3 && MessageBoxW(view.host, Text("Installer_CancelConfirm").c_str(), Product, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
    if (!view.operation.Pending()) { DestroyWindow(view.host); return; }
    view.cancelAccepted = true;
    view.operation.Cancel();
    view.options.cancellation->store(true);
    Handle signal(CreateFileW(view.options.cancelFile.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr));
    Render(view);
}
static LRESULT CALLBACK Dispatch(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    auto view = reinterpret_cast<View*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        view = static_cast<View*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
        view->host = window;
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(view));
    }
    if (!view) return DefWindowProcW(window, message, wParam, lParam);
    try {
        switch (message) {
            case WM_CREATE: Appearance(*view); Render(*view); return 0;
            case WM_ERASEBKGND: return 1;
            case WM_PAINT: Paint(*view); return 0;
            case WM_DRAWITEM: DrawButton(*view, *reinterpret_cast<DRAWITEMSTRUCT*>(lParam)); return TRUE;
            case WM_CTLCOLORSTATIC: case WM_CTLCOLOREDIT: {
                HDC context = reinterpret_cast<HDC>(wParam);
                HWND control = reinterpret_cast<HWND>(lParam);
                bool surface = message == WM_CTLCOLOREDIT;
                SetBkMode(context, TRANSPARENT);
                COLORREF foreground = GetDlgCtrlID(control) == Status && view->operation.phase == OperationPhase::Failed ? view->colors.error
                    : GetPropW(control, L"EGL.SecondaryText") && !view->colors.contrast ? view->colors.muted : view->colors.text;
                SetTextColor(context, foreground);
                SetBkColor(context, surface ? view->colors.surface : view->colors.background);
                return reinterpret_cast<LRESULT>(surface ? view->surface : view->background);
            }
            case DM_GETDEFID: return MAKELRESULT(view->defaultButton, DC_HASDEFID);
            case DM_SETDEFID: view->defaultButton = static_cast<int>(wParam); return TRUE;
            case WM_COMMAND: {
                int id = LOWORD(wParam);
                if (id == Cancel) { CancelOperation(*view); return 0; }
                if (view->operation.Pending()) return 0;
                if (view->page == 1 && id == Directory && HIWORD(wParam) == EN_KILLFOCUS) { try { SavePath(*view, true); } catch (const std::exception& error) { Log(Wide(error.what())); } return 0; }
                if (id == Back) { if (view->page == 1) SavePath(*view, false); view->page = view->uninstall || view->options.update ? 0 : view->page - 1; Render(*view); return 0; }
                if (id == BrowseDirectory) { Browse(*view); return 0; }
                if (id == KeepData && HIWORD(wParam) == BN_CLICKED) { view->options.keepData = SendMessageW(reinterpret_cast<HWND>(lParam), BM_GETCHECK, 0, 0) == BST_CHECKED; return 0; }
                if (id == OpenDirectory) { ShellExecuteW(window, L"open", view->options.root.c_str(), nullptr, nullptr, SW_SHOWNORMAL); DestroyWindow(window); return 0; }
                if (id == OpenApplication) { auto path = fs::path(view->options.root) / L"EricGameLauncher.exe"; ShellExecuteW(window, L"open", path.c_str(), nullptr, view->options.root.c_str(), SW_SHOWNORMAL); DestroyWindow(window); return 0; }
                if (id != Action && id != Extract) return 0;
                if (view->page == 0) {
                    if (view->uninstall) view->page = 2;
                    else {
                        view->options = view->choices;
                        if (id == Extract) { view->options.extract = true; view->options.update = false; view->page = 1; }
                        else { view->installed = SelectInstallation(view->options); view->page = view->installed ? 2 : 1; }
                    }
                    Render(*view);
                }
                else if (view->page == 1) { SavePath(*view, true); view->page = 2; Render(*view); }
                else if (view->page == 2) Begin(*view);
                return 0;
            }
            case WM_APP + 1: {
                std::unique_ptr<Result> result(reinterpret_cast<Result*>(lParam));
                if (!view->operation.Pending()) return 0;
                view->operation.Finish(result->cancelled, result->failure.empty());
                DeleteFileW(view->options.cancelFile.c_str());
                if (view->operation.phase == OperationPhase::Cancelled) { DestroyWindow(window); return 0; }
                view->result = view->operation.phase == OperationPhase::Succeeded ? 0 : 1;
                view->page = 3;
                Render(*view);
                return 0;
            }
            case WM_SETTINGCHANGE: case WM_THEMECHANGED:
                if (view->page == 1) SavePath(*view, false);
                Appearance(*view); Render(*view); return 0;
            case WM_DPICHANGED: {
                if (view->page == 1) SavePath(*view, false);
                ClearControls(*view);
                view->dpi = HIWORD(wParam);
                Fonts(*view);
                auto position = reinterpret_cast<RECT*>(lParam);
                SetWindowPos(window, nullptr, position->left, position->top, position->right - position->left, position->bottom - position->top, SWP_NOZORDER | SWP_NOACTIVATE);
                Render(*view);
                return 0;
            }
            case WM_CLOSE: CancelOperation(*view); return 0;
            case WM_DESTROY: PostQuitMessage(0); return 0;
        }
    } catch (const std::exception& error) {
        Log(Wide(error.what()));
        MessageBoxW(window, Text(view->page == 1 ? "Installer_InvalidDirectory" : "Installer_Failed").c_str(), Product, MB_OK | MB_ICONERROR);
    }
    return DefWindowProcW(window, message, wParam, lParam);
}
int Window(Options options, const std::function<void(const Options&)>& execute, bool uninstall) {
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS };
    Check(InitCommonControlsEx(&controls) != 0, "InitCommonControlsEx");
    View view;
    view.options = options; view.choices = options; view.choices.update = false; view.execute = execute; view.uninstall = uninstall;
    view.dpi = static_cast<int>(GetDpiForSystem());
    Fonts(view);
    WNDCLASSEXW registration{ sizeof(registration) };
    registration.lpfnWndProc = Dispatch; registration.hInstance = GetModuleHandleW(nullptr);
    registration.hIcon = LoadIconW(registration.hInstance, MAKEINTRESOURCEW(100)); registration.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    registration.lpszClassName = L"EricGameLauncher.ModernSetup";
    Check(RegisterClassExW(&registration) != 0, "RegisterWindowClass");
    RECT rectangle{0, 0, Scale(view, 560), Scale(view, view.height)};
    DWORD style = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN;
    AdjustWindowRectExForDpi(&rectangle, style, FALSE, WS_EX_CONTROLPARENT, view.dpi);
    int width = rectangle.right - rectangle.left, height = rectangle.bottom - rectangle.top;
    auto caption = Text(uninstall ? "Installer_UninstallWindowTitle" : "Installer_WindowTitle");
    HWND window = CreateWindowExW(WS_EX_CONTROLPARENT, registration.lpszClassName, caption.c_str(), style, (GetSystemMetrics(SM_CXSCREEN) - width) / 2, (GetSystemMetrics(SM_CYSCREEN) - height) / 2, width, height, nullptr, nullptr, registration.hInstance, &view);
    Check(window != nullptr, "CreateInstallerWindow");
    ShowWindow(window, SW_SHOW);
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) if (!IsDialogMessageW(window, &message)) { TranslateMessage(&message); DispatchMessageW(&message); }
    ClearControls(view);
    for (HFONT font : {view.font, view.titleFont, view.metadataFont, view.emphasisFont}) if (font) DeleteObject(font);
    if (view.icon) DestroyIcon(view.icon);
    DeleteObject(view.background); DeleteObject(view.surface);
    return view.result;
}
}
