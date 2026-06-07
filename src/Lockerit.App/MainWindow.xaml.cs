using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lockerit.App.Security;
using Lockerit.Core;
using Lockerit.Core.Models;
using Lockerit.Core.Security;
using Lockerit.Core.Storage;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Lockerit.App;

public partial class MainWindow : Window
{
    private const string DefaultCategory = "General";

    private readonly ObservableCollection<PasswordSecret> _passwords = [];
    private readonly List<PasswordSecret> _allPasswords = [];
    private readonly AppSettingsStore _settingsStore = new();
    private readonly WindowsAccountInfo _account;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings;
    private LockeritVault? _vault;
    private PasswordSecret? _selectedSecret;
    private bool _allowExit;
    private bool _isApplyingSettings;
    private bool _isComponentReady;
    private bool _isPasswordRevealed;

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        _account = WindowsAccountContext.Current();

        InitializeComponent();
        _isComponentReady = true;

        PasswordList.ItemsSource = _passwords;
        UnlockWindowsUserText.Text = _account.DisplayName;
        var accountInitials = GetAccountInitials(_account.DisplayName);
        AccountInitialsText.Text = accountInitials;
        AccountMenuInitialsText.Text = accountInitials;
        AccountMenuNameText.Text = _account.DisplayName;

        ConfigureTrayIcon();
        ApplySettingsToUi();
        ClearForm();
        SetUnlockedState(isUnlocked: false);
        SetStatus("Locked. Verify your Windows account to unlock.");
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit && _settings.HideToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vault?.Dispose();
        _trayIcon?.Dispose();
        SetPasswordText(string.Empty);
        base.OnClosed(e);
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        UnlockButton.IsEnabled = false;

        try
        {
            var verification = await WindowsCredentialVerifier.VerifyCurrentAccountAsync(this, _account);
            if (!verification.Verified)
            {
                UnlockButton.IsEnabled = true;
                if (verification.Cancelled)
                {
                    SetStatus("Unlock cancelled.");
                }
                else
                {
                    ShowWarning("Windows authorization required.", verification.ErrorMessage ?? "Windows could not verify this account.");
                    SetStatus("Windows sign-in failed.");
                }

                return;
            }

            var paths = ResolveVaultPaths();
            _vault = LockeritVault.UnlockWithCurrentWindowsUser(paths);

            LoginSurface.Visibility = Visibility.Collapsed;
            ShellSurface.Visibility = Visibility.Visible;
            ShowVaultContent();

            LoadPasswords();
            SetUnlockedState(isUnlocked: true);
            SetStatus(_vault.CreatedNewKey ? "Vault initialized and unlocked." : "Vault unlocked.");
        }
        catch (Exception ex)
        {
            UnlockButton.IsEnabled = true;
            ShowError("Unlock failed.", ex);
            SetUnlockedState(isUnlocked: false);
            SetStatus("Unlock failed.");
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _allowExit = true;
        Close();
    }

    private void HideToTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void OpenVaultFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var paths = _vault?.Paths ?? ResolveVaultPaths();
            Directory.CreateDirectory(paths.RootDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = paths.RootDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError("Could not open the vault folder.", ex);
        }
    }

    private void OpenReadmeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var readme = FindRepositoryFile("README.md");
            Process.Start(new ProcessStartInfo
            {
                FileName = readme,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError("Could not open README.", ex);
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || LanguageComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        SaveLanguage(item.Tag?.ToString() ?? "en");
    }

    private void HideToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings = _settings with { HideToTrayOnClose = HideToTrayCheckBox.IsChecked == true };
        _settingsStore.Save(_settings);
        SetStatus("Tray preference saved.");
    }

    private void BrowseVaultPathButton_Click(object sender, RoutedEventArgs e)
    {
        var current = ResolveVaultPaths();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".db",
            Filter = "Lockerit vault database (*.db)|*.db|All files (*.*)|*.*",
            InitialDirectory = current.RootDirectory,
            FileName = Path.GetFileName(current.DatabasePath),
            OverwritePrompt = false,
            Title = "Choose Lockerit vault database"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _settings = _settings with { VaultDatabasePath = dialog.FileName };
        _settingsStore.Save(_settings);
        ApplySettingsToUi();
        SetStatus(_vault is null
            ? "Vault path saved."
            : "Vault path saved. Lock and unlock to switch vaults.");
    }

    private void UseDefaultVaultPathButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { VaultDatabasePath = null };
        _settingsStore.Save(_settings);
        ApplySettingsToUi();
        SetStatus(_vault is null
            ? "Default vault path restored."
            : "Default vault path restored. Lock and unlock to switch vaults.");
    }

    private void LoginImportRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        ImportRecoveryKit(unlockAfterImport: true);
    }

    private void ExportRecoveryKitButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var vault = EnsureVault();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".lockerit-recovery.json",
                Filter = "Lockerit Recovery Kit (*.lockerit-recovery.json)|*.lockerit-recovery.json|JSON (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(vault.Paths.RootDirectory)
                    ? vault.Paths.RootDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                FileName = $"lockerit-recovery-{DateTime.Now:yyyyMMdd-HHmm}.lockerit-recovery.json",
                OverwritePrompt = true,
                Title = "Export Lockerit Recovery Kit"
            };

            if (dialog.ShowDialog(this) != true)
            {
                SetStatus("Recovery export cancelled.");
                return;
            }

            var passphrase = RecoveryPassphraseDialog.ShowForExport(this);
            if (passphrase is null)
            {
                SetStatus("Recovery export cancelled.");
                return;
            }

            var result = vault.ExportRecoveryKit(dialog.FileName, passphrase);
            SetRecoveryStatus($"Recovery Kit exported to {result.FilePath}. Keep it separate from the vault database and remember the recovery passphrase.");
            SetStatus("Recovery Kit exported.");
        }
        catch (Exception ex)
        {
            ShowError("Recovery export failed.", ex);
            SetStatus("Recovery export failed.");
        }
    }

    private void ImportRecoveryKitButton_Click(object sender, RoutedEventArgs e)
    {
        ImportRecoveryKit(unlockAfterImport: false);
    }

    private void ReprotectKeyringButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var vault = EnsureVault();
            vault.ReprotectWindowsKeyringForCurrentUser();
            SetRecoveryStatus($"Local keyring refreshed for {_account.DisplayName}. Keyring: {vault.Paths.KeyFilePath}");
            ApplyUnlockDiagnostics();
            SetStatus("Local keyring refreshed.");
        }
        catch (Exception ex)
        {
            ShowError("Keyring refresh failed.", ex);
            SetStatus("Keyring refresh failed.");
        }
    }

    private void ImportRecoveryKit(bool unlockAfterImport)
    {
        var paths = _vault?.Paths ?? ResolveVaultPaths();

        if (!File.Exists(paths.DatabasePath))
        {
            ShowWarning(
                "Vault database required.",
                "Choose or copy the Lockerit vault database before importing a Recovery Kit.");
            SetStatus("Recovery import needs a vault database.");
            return;
        }

        if (File.Exists(paths.KeyFilePath) && !ConfirmReplaceLocalKeyring(paths))
        {
            SetStatus("Recovery import cancelled.");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Lockerit Recovery Kit (*.lockerit-recovery.json)|*.lockerit-recovery.json|JSON (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(paths.RootDirectory)
                ? paths.RootDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Title = "Import Lockerit Recovery Kit"
        };

        if (dialog.ShowDialog(this) != true)
        {
            SetStatus("Recovery import cancelled.");
            return;
        }

        var passphrase = RecoveryPassphraseDialog.ShowForImport(this);
        if (passphrase is null)
        {
            SetStatus("Recovery import cancelled.");
            return;
        }

        try
        {
            var result = LockeritVault.ImportRecoveryKitForCurrentWindowsUser(paths, dialog.FileName, passphrase);
            SetRecoveryStatus($"Recovery Kit imported. A new Windows-protected keyring was created for {_account.DisplayName}. Kit created: {result.CreatedAtUtc.LocalDateTime:g}.");

            if (_vault is null || unlockAfterImport)
            {
                OpenVaultAfterRecoveryImport(paths);
                return;
            }

            ApplyUnlockDiagnostics();
            SetStatus("Recovery Kit imported and local keyring updated.");
        }
        catch (Exception ex)
        {
            ShowError("Recovery import failed.", ex);
            SetStatus("Recovery import failed.");
        }
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isComponentReady)
        {
            return;
        }

        ApplyPasswordFilter();
    }

    private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isComponentReady)
        {
            return;
        }

        ApplyPasswordFilter();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = true;
    }

    private void AccountLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        LockVault("Logged out.");
    }

    private void VaultNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vault is null)
        {
            ShellSurface.Visibility = Visibility.Collapsed;
            LoginSurface.Visibility = Visibility.Visible;
            SetStatus("Unlock the vault first.");
            return;
        }

        ShowVaultContent();
        SetStatus("Vault workspace.");
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vault is null)
        {
            ShellSurface.Visibility = Visibility.Collapsed;
            LoginSurface.Visibility = Visibility.Visible;
            SetStatus("Unlock the vault first.");
            return;
        }

        ShowSettingsContent();
        SetStatus("Settings workspace.");
    }

    private void LoadPasswords(Guid? selectId = null)
    {
        var vault = EnsureVault();

        _allPasswords.Clear();
        _passwords.Clear();
        foreach (var secret in vault.ListPasswords())
        {
            _allPasswords.Add(secret);
        }

        ApplyPasswordFilter();

        if (selectId is { } id)
        {
            PasswordList.SelectedItem = _passwords.FirstOrDefault(secret => secret.Id == id);
        }
    }

    private void ApplyPasswordFilter()
    {
        if (!_isComponentReady ||
            EntryCountText is null ||
            EmptyPasswordsPanel is null ||
            PasswordList is null)
        {
            return;
        }

        var query = SearchInput?.Text?.Trim();
        var category = GetSelectedCategoryFilter();
        var filtered = _allPasswords.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(secret =>
                Contains(secret.Title, query) ||
                Contains(secret.Category, query) ||
                Contains(secret.UserName, query) ||
                Contains(secret.Url, query) ||
                Contains(secret.Notes, query));
        }

        if (!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(secret =>
                string.Equals(NormalizeCategory(secret.Category), category, StringComparison.OrdinalIgnoreCase));
        }

        var selectedId = _selectedSecret?.Id;

        _passwords.Clear();
        foreach (var secret in filtered.ToList())
        {
            _passwords.Add(secret);
        }

        EntryCountText.Text = _passwords.Count == 1 ? "1 item" : $"{_passwords.Count} items";
        EmptyPasswordsPanel.Visibility = _passwords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectedId is { } id)
        {
            PasswordList.SelectedItem = _passwords.FirstOrDefault(secret => secret.Id == id);
        }
    }

    private void PasswordList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordList.SelectedItem is not PasswordSecret secret)
        {
            return;
        }

        _selectedSecret = secret;
        SetStatus("Entry highlighted.");
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vault is null)
        {
            SetStatus("Unlock the vault first.");
            return;
        }

        PasswordList.SelectedItem = null;
        ClearForm();
        OpenEntryModal(null);
        TitleInput.Focus();
        SetStatus("New entry.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var vault = EnsureVault();
            var password = ReadPassword();

            if (string.IsNullOrWhiteSpace(TitleInput.Text))
            {
                TitleInput.Focus();
                SetStatus("Title is required.");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                PasswordInput.Focus();
                SetStatus("Password is required.");
                return;
            }

            var category = GetSelectedEntryCategory();
            var secret = _selectedSecret is null
                ? PasswordSecret.Create(TitleInput.Text, category, UserNameInput.Text, password, UrlInput.Text, NotesInput.Text)
                : _selectedSecret.Update(TitleInput.Text, category, UserNameInput.Text, password, UrlInput.Text, NotesInput.Text);

            vault.SavePassword(secret);
            LoadPasswords(secret.Id);
            CloseEntryModal();
            SetStatus("Password saved.");
        }
        catch (Exception ex)
        {
            ShowError("Save failed.", ex);
            SetStatus("Save failed.");
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSecret is null)
        {
            return;
        }

        DeleteSecret(_selectedSecret);
    }

    private void DeleteSecret(PasswordSecret secret)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"Delete \"{secret.Title}\"?",
            "Lockerit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            EnsureVault().DeletePassword(secret.Id);
            LoadPasswords();
            ClearForm();
            CloseEntryModal();
            SetStatus("Password deleted.");
        }
        catch (Exception ex)
        {
            ShowError("Delete failed.", ex);
            SetStatus("Delete failed.");
        }
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        SetPasswordReveal(!_isPasswordRevealed);
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        SetPasswordText(GeneratePassword(24));
        SetStatus("Password generated.");
    }

    private void CopyUserButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(UserNameInput.Text, "Username copied.");
    }

    private void CopyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(ReadPassword(), "Password copied.");
    }

    private void CopyUserRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSecretFromSender(sender) is { } secret)
        {
            CopyText(secret.UserName, "Username copied.");
        }
    }

    private void CopyPasswordRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSecretFromSender(sender) is { } secret)
        {
            CopyText(secret.Password, "Password copied.");
        }
    }

    private void EditPasswordRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSecretFromSender(sender) is { } secret)
        {
            OpenEntryModal(secret);
            SetStatus("Editing entry.");
        }
    }

    private void DeletePasswordRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSecretFromSender(sender) is { } secret)
        {
            DeleteSecret(secret);
        }
    }

    private void CancelEntryButton_Click(object sender, RoutedEventArgs e)
    {
        CloseEntryModal();
        SetStatus("Entry edit cancelled.");
    }

    private void OpenEntryModal(PasswordSecret? secret)
    {
        _selectedSecret = secret;

        if (secret is null)
        {
            ClearForm();
            ModalTitleText.Text = "New password";
            DeleteButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModalTitleText.Text = "Edit password";
            TitleInput.Text = secret.Title;
            SelectComboBoxValue(CategoryInput, NormalizeCategory(secret.Category));
            UserNameInput.Text = secret.UserName;
            SetPasswordText(secret.Password);
            UrlInput.Text = secret.Url;
            NotesInput.Text = secret.Notes;
            DeleteButton.IsEnabled = true;
            DeleteButton.Visibility = Visibility.Visible;
            SetPasswordReveal(false);
            PasswordList.SelectedItem = _passwords.FirstOrDefault(item => item.Id == secret.Id);
        }

        EntryModalOverlay.Visibility = Visibility.Visible;
        TitleInput.Focus();
    }

    private void CloseEntryModal()
    {
        if (EntryModalOverlay is null)
        {
            return;
        }

        EntryModalOverlay.Visibility = Visibility.Collapsed;
        ClearForm();
    }

    private void LockVault(string status)
    {
        _vault?.Dispose();
        _vault = null;
        _allPasswords.Clear();
        _passwords.Clear();
        PasswordList.SelectedItem = null;
        SearchInput.Text = string.Empty;
        CategoryFilterComboBox.SelectedIndex = 0;
        ClearForm();
        CloseEntryModal();
        AccountPopup.IsOpen = false;

        ShellSurface.Visibility = Visibility.Collapsed;
        LoginSurface.Visibility = Visibility.Visible;
        UnlockButton.IsEnabled = true;

        SetUnlockedState(isUnlocked: false);
        SetStatus(status);
    }

    private void ClearForm()
    {
        _selectedSecret = null;
        TitleInput.Text = string.Empty;
        SelectComboBoxValue(CategoryInput, DefaultCategory);
        UserNameInput.Text = string.Empty;
        SetPasswordText(string.Empty);
        UrlInput.Text = string.Empty;
        NotesInput.Text = string.Empty;
        DeleteButton.IsEnabled = false;
        DeleteButton.Visibility = Visibility.Collapsed;
        SetPasswordReveal(false);
    }

    private string ReadPassword()
    {
        return _isPasswordRevealed ? PasswordRevealInput.Text : PasswordInput.Password;
    }

    private void SetPasswordText(string value)
    {
        PasswordInput.Password = value;
        PasswordRevealInput.Text = value;
    }

    private void SetPasswordReveal(bool reveal)
    {
        if (reveal)
        {
            PasswordRevealInput.Text = PasswordInput.Password;
        }
        else
        {
            PasswordInput.Password = PasswordRevealInput.Text;
        }

        _isPasswordRevealed = reveal;
        PasswordInput.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        PasswordRevealInput.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        RevealButton.ToolTip = reveal ? "Hide password" : "Show password";
        if (RevealButton.Content is TextBlock label)
        {
            label.Text = reveal ? "Hide" : "Show";
        }
    }

    private void SetUnlockedState(bool isUnlocked)
    {
        if (SearchInput is not null)
        {
            SearchInput.IsEnabled = isUnlocked;
        }

        ApplyUnlockDiagnostics();
        ApplyNavigationState();
    }

    private void ApplyNavigationState()
    {
        if (VaultNavButton is null || SettingsNavButton is null || VaultContent is null || SettingsContent is null)
        {
            return;
        }

        var isVault = VaultContent.Visibility == Visibility.Visible;
        ApplyNavButtonState(VaultNavButton, isVault);
        ApplyNavButtonState(SettingsNavButton, SettingsContent.Visibility == Visibility.Visible);
    }

    private void ApplyNavButtonState(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = selected ? BrushFrom("#20211E") : System.Windows.Media.Brushes.Transparent;
        button.BorderBrush = selected ? (System.Windows.Media.Brush)FindResource("BorderBrushSoft") : System.Windows.Media.Brushes.Transparent;
        button.Foreground = selected
            ? (System.Windows.Media.Brush)FindResource("InkBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void ShowVaultContent()
    {
        AccountPopup.IsOpen = false;
        VaultContent.Visibility = Visibility.Visible;
        SettingsContent.Visibility = Visibility.Collapsed;
        ApplyNavigationState();
    }

    private void ShowSettingsContent()
    {
        AccountPopup.IsOpen = false;
        CloseEntryModal();
        VaultContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Visible;
        ApplyNavigationState();
    }

    private void ApplySettingsToUi()
    {
        _isApplyingSettings = true;
        try
        {
            var languageCode = string.IsNullOrWhiteSpace(_settings.LanguageCode)
                ? "en"
                : _settings.LanguageCode;

            foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            HideToTrayCheckBox.IsChecked = _settings.HideToTrayOnClose;
            ApplyVaultPathToUi();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void ApplyVaultPathToUi()
    {
        var paths = ResolveVaultPaths();
        VaultPathInput.Text = paths.DatabasePath;
        UnlockVaultPathText.Text = paths.DatabasePath;
        VaultPathStatusText.Text = paths.DatabasePath;
        SetRecoveryStatus($"Vault database: {paths.DatabasePath}. Local keyring: {paths.KeyFilePath}");

        ApplyUnlockDiagnostics();
    }

    private void ApplyUnlockDiagnostics()
    {
        if (UnlockDiagnosticText is null)
        {
            return;
        }

        var paths = _vault?.Paths ?? ResolveVaultPaths();
        var state = _vault is null ? "Locked" : "Unlocked";
        var keyState = _vault is null
            ? "The protected key has not been opened in this session."
            : "The protected key was unsealed by Windows DPAPI for the current user.";

        UnlockDiagnosticText.Text =
            $"{state}. Account: {_account.DisplayName}. SID: {_account.Sid ?? "unavailable"}. Vault: {paths.DatabasePath}. {keyState}";
    }

    private void SaveLanguage(string languageCode)
    {
        _settings = _settings with { LanguageCode = languageCode };
        _settingsStore.Save(_settings);
        ApplySettingsToUi();

        var languageName = string.Equals(languageCode, "pt-BR", StringComparison.OrdinalIgnoreCase)
            ? "Portuguese"
            : "English";

        SetStatus($"Language preference saved: {languageName}.");
    }

    private LockeritPaths ResolveVaultPaths()
    {
        try
        {
            return string.IsNullOrWhiteSpace(_settings.VaultDatabasePath)
                ? LockeritPaths.ForCurrentUser()
                : LockeritPaths.ForDatabaseFile(_settings.VaultDatabasePath);
        }
        catch
        {
            return LockeritPaths.ForCurrentUser();
        }
    }

    private bool ConfirmReplaceLocalKeyring(LockeritPaths paths)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"Replace the local Windows keyring for {_account.DisplayName}?\n\nVault: {paths.DatabasePath}\nKeyring: {paths.KeyFilePath}\n\nThis does not import another Windows user. It only stores the recovered vault key for the currently signed-in Windows account.",
            "Lockerit Recovery",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void OpenVaultAfterRecoveryImport(LockeritPaths paths)
    {
        _vault?.Dispose();
        _vault = LockeritVault.UnlockWithCurrentWindowsUser(paths);

        LoginSurface.Visibility = Visibility.Collapsed;
        ShellSurface.Visibility = Visibility.Visible;
        ShowVaultContent();
        LoadPasswords();
        SetUnlockedState(isUnlocked: true);
        UnlockButton.IsEnabled = true;
        SetStatus("Recovery Kit imported and vault unlocked.");
    }

    private void ConfigureTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Lockerit",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Items.Add("Show Lockerit", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _trayIcon.ContextMenuStrip.Items.Add("Lock and hide", null, (_, _) => Dispatcher.Invoke(() =>
        {
            LockVault("Vault locked and hidden to tray.");
            HideToTray();
        }));
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _allowExit = true;
            Close();
        }));
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void HideToTray()
    {
        if (_trayIcon is null)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(1200, "Lockerit", "Still running in the Windows tray.", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        SetStatus("Restored from tray.");
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);

        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(14, 15, 13));
        using var primary = new Drawing.SolidBrush(Drawing.Color.FromArgb(217, 119, 87));
        using var accent = new Drawing.SolidBrush(Drawing.Color.FromArgb(122, 167, 217));
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(217, 119, 87), 2);

        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawEllipse(pen, 2, 2, 28, 28);
        graphics.FillRectangle(primary, 10, 14, 12, 10);
        graphics.FillRectangle(primary, 12, 10, 8, 5);
        graphics.FillEllipse(background, 14, 17, 4, 4);
        graphics.FillRectangle(background, 15, 20, 2, 4);
        graphics.FillRectangle(accent, 22, 9, 6, 2);
        graphics.FillRectangle(accent, 24, 7, 2, 6);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static string GeneratePassword(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%*?-_";
        var password = new char[length];

        for (var i = 0; i < password.Length; i++)
        {
            password[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(password);
    }

    private void CopyText(string value, string status)
    {
        if (string.IsNullOrEmpty(value))
        {
            SetStatus("Nothing to copy.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(value);
            _ = ClearClipboardIfUnchangedAsync(value);
            SetStatus(status);
        }
        catch (ExternalException ex)
        {
            ShowError("Clipboard access failed.", ex);
        }
    }

    private static async Task ClearClipboardIfUnchangedAsync(string value)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText() && System.Windows.Clipboard.GetText() == value)
                {
                    System.Windows.Clipboard.Clear();
                }
            }
            catch (ExternalException)
            {
                // Another process can temporarily own the clipboard.
            }
        });
    }

    private LockeritVault EnsureVault()
    {
        return _vault ?? throw new InvalidOperationException("The Lockerit vault is locked.");
    }

    private void SetStatus(string message)
    {
        var status = $"{DateTime.Now:HH:mm:ss}  {message}";

        if (StatusText is not null)
        {
            StatusText.Text = status;
        }

        if (ShellStatusText is not null)
        {
            ShellStatusText.Text = status;
        }
    }

    private void SetRecoveryStatus(string message)
    {
        if (RecoveryStatusText is not null)
        {
            RecoveryStatusText.Text = message;
        }
    }

    private void ShowError(string title, Exception exception)
    {
        System.Windows.MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void ShowWarning(string title, string message)
    {
        System.Windows.MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static System.Windows.Media.Brush BrushFrom(string color)
    {
        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static bool Contains(string value, string query)
    {
        return !string.IsNullOrEmpty(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private string GetSelectedCategoryFilter()
    {
        return CategoryFilterComboBox?.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? "All"
            : "All";
    }

    private string GetSelectedEntryCategory()
    {
        return CategoryInput?.SelectedItem is ComboBoxItem item
            ? NormalizeCategory(item.Tag?.ToString() ?? item.Content?.ToString() ?? DefaultCategory)
            : DefaultCategory;
    }

    private static string NormalizeCategory(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultCategory : value.Trim();
    }

    private static PasswordSecret? GetSecretFromSender(object sender)
    {
        return (sender as FrameworkElement)?.Tag as PasswordSecret;
    }

    private static void SelectComboBoxValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            var itemValue = item.Tag?.ToString() ?? item.Content?.ToString();
            if (string.Equals(itemValue, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static string GetAccountInitials(string displayName)
    {
        var parts = displayName
            .Split(['\\', '/', ' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return parts.Length == 0 ? "U" : new string(parts);
    }

    private static string FindRepositoryFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(fileName);
    }
}
