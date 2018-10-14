﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using HoloLensAppManager.Helpers;
using HoloLensAppManager.Models;
using HoloLensAppManager.Services;
using HoloLensAppManager.Views;
using Microsoft.Tools.WindowsDevicePortal;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage;
using Windows.UI.Core;

namespace HoloLensAppManager.ViewModels
{
    public class AppInfoForInstall : Observable
    {
        public AppInfo AppInfo;

        public List<AppVersion> SortedVersions
        {
            get
            {
                if (AppInfo.Versions == null)
                {
                    return new List<AppVersion>();
                }
                var list = AppInfo.Versions.ToList();
                list.Sort();
                list.Reverse();
                return list;
            }
        }

        private AppVersion selectedVersion;
        public AppVersion SelectedVersion
        {
            get { return selectedVersion; }
            set
            {
                this.Set(ref this.selectedVersion, value);
            }
        }

        public void SelectLatestVersion()
        {
            OnPropertyChanged("SortedVersions");
            if (SortedVersions.Count > 0)
            {
                SelectedVersion = SortedVersions[0];
            }
            else
            {
                SelectedVersion = null;
            }
        }
    }

    public class InstallViewModel : Observable
    {
        private ObservableCollection<AppInfoForInstall> appInfoList = new ObservableCollection<AppInfoForInstall>();

        public ObservableCollection<AppInfoForInstall> AppInfoList
        {
            get
            {
                return appInfoList;
            }
        }

        private int versionIndex;
        public int VersionIndex
        {
            get { return versionIndex; }
            set
            {
                this.Set(ref this.versionIndex, value);
            }
        }


        #region HoloLens 接続用プロパティ
        private string address;
        public string Address
        {
            get { return address; }
            set
            {
                this.Set(ref this.address, value);
                localSettings.Values[AddressSettingKey] = value;
                ((RelayCommand)ConnectCommand).OnCanExecuteChanged();
            }
        }

        private bool usbConnection;
        public bool UsbConnection
        {
            get { return usbConnection; }
            set
            {
                this.Set(ref this.usbConnection, value);
                localSettings.Values[UsbConnectionSettingKey] = value.ToString();
                ((RelayCommand)ConnectCommand).OnCanExecuteChanged();
                AddressEnabled = !usbConnection;
            }
        }

        private bool addressEnabled;
        public bool AddressEnabled
        {
            get { return addressEnabled; }
            set
            {
                this.Set(ref this.addressEnabled, value);
            }
        }

        private string username;
        public string Username
        {
            get { return username; }
            set
            {
                this.Set(ref this.username, value);
                localSettings.Values[UsernameSettingKey] = value;
                ((RelayCommand)ConnectCommand).OnCanExecuteChanged();
            }
        }

        private string password;
        public string Password
        {
            get { return password; }
            set
            {
                this.Set(ref this.password, value);
                localSettings.Values[PasswordSettingKey] = value;
                ((RelayCommand)ConnectCommand).OnCanExecuteChanged();
            }
        }

        private string errorMessage;
        public string ErrorMessage
        {
            get { return errorMessage; }
            set
            {
                this.Set(ref this.errorMessage, value);
            }
        }

        private string successMessage;
        public string SuccessMessage
        {
            get { return successMessage; }
            set
            {
                this.Set(ref this.successMessage, value);
            }
        }
        #endregion


        #region コマンド
        private ICommand connectCommand;
        public ICommand ConnectCommand => connectCommand ?? (connectCommand =
            new RelayCommand(async () => { await ConnectToDevice(); }, CanExecuteConnect));

        private bool CanExecuteConnect()
        {
            return (connectionStatus == ConnectionState.NotConnected || connectionStatus == ConnectionState.Connected)
                && ( !String.IsNullOrWhiteSpace(Address) || usbConnection)
                && !String.IsNullOrWhiteSpace(Username)
                && !String.IsNullOrWhiteSpace(Password);
        }

        private ICommand installCommand;
        public ICommand InstallCommand => installCommand ?? (installCommand =
            new RelayCommand<AppInfoForInstall>(async (app) => { await InstallApplication(app); }));

        private ICommand editCommand;
        public ICommand EditCommand => editCommand ?? (editCommand =
            new RelayCommand<AppInfoForInstall>(async (app) => { await EditApplication(app); }));


        #endregion

        #region 設定値
        ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
        const string AddressSettingKey = "DeviceAddress";
        const string UsbConnectionSettingKey = "DeviceUsbConnection";
        const string UsernameSettingKey = "DeviceUserName";
        const string PasswordSettingKey = "DevicePassword";
        #endregion


        public enum ConnectionState
        {
            NotConnected, Connecting, Connected
        }

        ConnectionState connectionStatus = ConnectionState.NotConnected;
        public ConnectionState ConnectionStatus
        {
            get
            {
                return connectionStatus;
            }
            set
            {
                connectionStatus = value;
                ((RelayCommand)ConnectCommand).OnCanExecuteChanged();
            }
        }


        AzureStorageUploader uploader;
        DevicePortal portal;
        BusyIndicator indicator;

        public InstallViewModel()
        {
            // 接続情報の設定
            Address = LoadSettingData(localSettings, AddressSettingKey);
            try
            {
                UsbConnection = Convert.ToBoolean(LoadSettingData(localSettings, UsbConnectionSettingKey));
            }
            catch (Exception)
            {
                UsbConnection = false;
            }
            Username = LoadSettingData(localSettings, UsernameSettingKey);
            Password = LoadSettingData(localSettings, PasswordSettingKey);

            uploader = new AzureStorageUploader();

            UpdateApplicationList();

            indicator = new BusyIndicator()
            {
                Message = "ただいま処理中です。しばらくお待ちください..."
            };
        }

        private string LoadSettingData(ApplicationDataContainer setting, string key)
        {
            object val = localSettings.Values[key];
            if (val != null && val is string)
            {
                return(string)val;
            }
            return "";
        }


        public async Task UpdateApplicationList()
        {
            var list = await uploader.GetAppInfoListAsync();
            appInfoList.Clear();
            foreach(var app in list)
            {
                appInfoList.Add(new AppInfoForInstall()
                {
                    AppInfo = app
                }
                );
            }

            foreach (var app in appInfoList)
            {
                app.SelectLatestVersion();
            }
        }

        private async Task InstallApplication(AppInfoForInstall appForInstall)
        {
            if (appForInstall == null)
            {
                return;
            }
            indicator = new BusyIndicator()
            {
                Message = $"{appForInstall.AppInfo.Name} をダウンロードしています。しばらくお待ちください..."
            };
            indicator.Show();

            ErrorMessage = "";
            SuccessMessage = $"{appForInstall.AppInfo.Name} をダウンロードしています";

            var app = await uploader.Download(appForInstall.AppInfo.Name, appForInstall.SelectedVersion.ToString());
            if (app == null)
            {
                ErrorMessage = $"{appForInstall.AppInfo.Name} のダウンロードに失敗しました";
                SuccessMessage = "";
                indicator.Hide();
            }
            else
            {
                var result = await ConnectToDevice();
                if (result)
                {
                    indicator.Hide();
                    indicator = new BusyIndicator()
                    {
                        Message = $"{appForInstall.AppInfo.Name} をインストールしています。しばらくお待ちください..."
                    };
                    indicator.Show();

                    SuccessMessage = $"{appForInstall.AppInfo.Name} をインストールしています";
                    ErrorMessage = "";
                    await InstallPackageAsync(app);
                    indicator.Hide();
                }
            }
        }

        private async Task EditApplication(AppInfoForInstall app)
        {
            NavigationService.Navigate(typeof(EditApplicationPage), app);
        }

        private async Task InstallPackageAsync(Application app)
        {
            await portal?.InstallApplicationAsync("", app.AppPackage, app.Dependencies);
        }

        private async Task<bool> ConnectToDevice()
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                Console.WriteLine("Connecting...");
                ConnectionStatus = ConnectionState.Connecting;

                SuccessMessage = "接続中";
                ErrorMessage = "";

            });

            string connectionAddress;
            Address = Address.Trim();

            if (UsbConnection)
            {
                connectionAddress = "http://127.0.0.1:10080";
            }
            else if (Address.StartsWith("127.0.0.1")) {
                connectionAddress = $"http://{Address}";
            }
            else
            {
                connectionAddress = $"https://{Address}";
            }

            bool allowUntrusted = true;

            portal = new DevicePortal(
                new DefaultDevicePortalConnection(connectionAddress, Username, Password));

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Connecting...");
            Console.WriteLine("Connecting...");

            var tcs = new TaskCompletionSource<bool>();

            portal.AppInstallStatus += async (p, eventArgs) =>
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                {
                    switch (eventArgs.Status)
                    {
                        case ApplicationInstallStatus.Completed:
                            SuccessMessage = eventArgs.Message;
                            ErrorMessage = "";
                            break;
                        case ApplicationInstallStatus.Failed:
                            SuccessMessage = "";
                            ErrorMessage = eventArgs.Message;
                            break;
                    }
                });
            };

            portal.ConnectionStatus += async (p, connectArgs) =>
            {
                if (connectArgs.Status == DeviceConnectionStatus.Connected)
                {
                    sb.Append("Connected to: ");
                    sb.AppendLine(p.Address);
                    sb.Append("OS version: ");
                    sb.AppendLine(p.OperatingSystemVersion);
                    sb.Append("Device family: ");
                    sb.AppendLine(p.DeviceFamily);
                    sb.Append("Platform: ");
                    sb.AppendLine(String.Format("{0} ({1})",
                        p.PlatformName,
                        p.Platform.ToString()));
                    tcs.SetResult(true);
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                    {
                        ConnectionStatus = ConnectionState.Connected;
                        SuccessMessage = "接続に成功しました";
                    });

                }
                else if(connectArgs.Status == DeviceConnectionStatus.Failed)
                {
                    //sb.AppendLine("Failed to connect to the device.");
                    //sb.AppendLine(connectArgs.Message);
                    tcs.SetResult(false);
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                    {
                        ConnectionStatus = ConnectionState.NotConnected;
                        SuccessMessage = "";
                        ErrorMessage = "接続に失敗しました";
                    });
                }
            };

            try
            {
                // If the user wants to allow untrusted connections, make a call to GetRootDeviceCertificate
                // with acceptUntrustedCerts set to true. This will enable untrusted connections for the
                // remainder of this session.
                Certificate certificate = null;
                if (allowUntrusted)
                {
                    certificate = await portal.GetRootDeviceCertificateAsync(true);
                }
                await portal.ConnectAsync(manualCertificate: certificate);
                return await tcs.Task;
            }
            catch (Exception exception)
            {
                sb.AppendLine(exception.Message);
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                {
                    ConnectionStatus = ConnectionState.NotConnected;
                    SuccessMessage = "";
                    ErrorMessage = "接続に失敗しました";
                    indicator.Hide();
                });
                return false;
            }
        }
    }
}
