﻿using ComputerVision;
using ICSharpCode.SharpZipLib.Zip;
using RX_Explorer.Class;
using RX_Explorer.Dialog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Devices.Input;
using Windows.Devices.Radios;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using TreeViewNode = Microsoft.UI.Xaml.Controls.TreeViewNode;

namespace RX_Explorer
{
    public sealed partial class FilePresenter : Page
    {
        public ObservableCollection<FileSystemStorageItem> FileCollection { get; private set; }

        private FileControl FileControlInstance;

        private int DropLock = 0;

        private int ViewDropLock = 0;

        private CancellationTokenSource HashCancellation;

        public StorageAreaWatcher AreaWatcher { get; private set; }

        private ListViewBase itemPresenter;
        public ListViewBase ItemPresenter
        {
            get
            {
                return itemPresenter;
            }
            set
            {
                if (value != itemPresenter)
                {
                    itemPresenter = value;

                    if (value is GridView)
                    {
                        GridViewControl.Visibility = Visibility.Visible;
                        ListViewControl.Visibility = Visibility.Collapsed;
                        GridViewControl.ItemsSource = FileCollection;
                        ListViewControl.ItemsSource = null;
                    }
                    else
                    {
                        ListViewControl.Visibility = Visibility.Visible;
                        GridViewControl.Visibility = Visibility.Collapsed;
                        ListViewControl.ItemsSource = FileCollection;
                        GridViewControl.ItemsSource = null;
                    }
                }
            }
        }

        private WiFiShareProvider WiFiProvider;
        private FileSystemStorageItem TabTarget = null;
        private FileSystemStorageItem CurrentNameEditItem = null;
        private DateTimeOffset LastClickTime;

        public FileSystemStorageItem SelectedItem
        {
            get
            {
                return ItemPresenter.SelectedItem as FileSystemStorageItem;
            }
            set
            {
                ItemPresenter.SelectedItem = value;
            }
        }

        public List<FileSystemStorageItem> SelectedItems
        {
            get
            {
                return ItemPresenter.SelectedItems.Select((Item) => Item as FileSystemStorageItem).ToList();
            }
        }

        public FilePresenter()
        {
            InitializeComponent();

            FileCollection = new ObservableCollection<FileSystemStorageItem>();
            FileCollection.CollectionChanged += FileCollection_CollectionChanged;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ZipStrings.CodePage = 936;

            Loaded += FilePresenter_Loaded;
            Unloaded += FilePresenter_Unloaded;
        }

        private void Current_Resuming(object sender, object e)
        {
            if (!FileControlInstance.IsNetworkDevice)
            {
                AreaWatcher.SetCurrentLocation(FileControlInstance.CurrentFolder?.Path);
            }
        }

        private void Current_Suspending(object sender, SuspendingEventArgs e)
        {
            if (!FileControlInstance.IsNetworkDevice)
            {
                AreaWatcher.SetCurrentLocation(null);
            }
        }

        private void FilePresenter_Unloaded(object sender, RoutedEventArgs e)
        {
            Application.Current.Suspending -= Current_Suspending;
            Application.Current.Resuming -= Current_Resuming;
            CoreWindow.GetForCurrentThread().KeyDown -= Window_KeyDown;
        }

        private void FilePresenter_Loaded(object sender, RoutedEventArgs e)
        {
            Application.Current.Suspending += Current_Suspending;
            Application.Current.Resuming += Current_Resuming;
            CoreWindow.GetForCurrentThread().KeyDown += Window_KeyDown;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is FileControl Instance)
            {
                FileControlInstance = Instance;

                AreaWatcher = new StorageAreaWatcher(FileCollection, FileControlInstance.FolderTree);

                if (!TabViewContainer.ThisPage.FFInstanceContainer.ContainsKey(Instance))
                {
                    TabViewContainer.ThisPage.FFInstanceContainer.Add(Instance, this);
                }
            }
        }

        private async void Window_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            var CtrlState = sender.GetKeyState(VirtualKey.Control);
            var ShiftState = sender.GetKeyState(VirtualKey.Shift);

            if (!FileControlInstance.IsSearchOrPathBoxFocused && !QueueContentDialog.IsRunningOrWaiting && !MainPage.ThisPage.IsAnyTaskRunning && SelectedItems.All((Item) => !Item.IsHidenItem))
            {
                args.Handled = true;

                switch (args.VirtualKey)
                {
                    case VirtualKey.Space when SelectedItem != null && SettingControl.IsQuicklookAvailable && SettingControl.IsQuicklookEnable:
                        {
                            await FullTrustExcutorController.Current.ViewWithQuicklookAsync(SelectedItem.Path).ConfigureAwait(false);
                            break;
                        }
                    case VirtualKey.Delete:
                        {
                            Delete_Click(null, null);
                            break;
                        }
                    case VirtualKey.F2:
                        {
                            Rename_Click(null, null);
                            break;
                        }
                    case VirtualKey.F5:
                        {
                            Refresh_Click(null, null);
                            break;
                        }
                    case VirtualKey.Enter when SelectedItem is FileSystemStorageItem Item:
                        {
                            await EnterSelectedItem(Item).ConfigureAwait(false);
                            break;
                        }
                    case VirtualKey.Back when FileControlInstance.Nav.CurrentSourcePageType.Name == nameof(FilePresenter) && FileControlInstance.GoBackRecord.IsEnabled:
                        {
                            FileControlInstance.GoBackRecord_Click(null, null);
                            break;
                        }
                    case VirtualKey.L when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            FileControlInstance.AddressBox.Focus(FocusState.Programmatic);
                            break;
                        }
                    case VirtualKey.V when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            Paste_Click(null, null);
                            break;
                        }
                    case VirtualKey.A when CtrlState.HasFlag(CoreVirtualKeyStates.Down) && SelectedItem == null:
                        {
                            ItemPresenter.SelectAll();
                            break;
                        }
                    case VirtualKey.C when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            Copy_Click(null, null);
                            break;
                        }
                    case VirtualKey.X when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            Cut_Click(null, null);
                            break;
                        }
                    case VirtualKey.D when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            Delete_Click(null, null);
                            break;
                        }
                    case VirtualKey.F when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            FileControlInstance.GlobeSearch.Focus(FocusState.Programmatic);
                            break;
                        }
                    case VirtualKey.N when ShiftState.HasFlag(CoreVirtualKeyStates.Down) && CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                        {
                            CreateFolder_Click(null, null);
                            break;
                        }
                    case VirtualKey.Z when CtrlState.HasFlag(CoreVirtualKeyStates.Down) && OperationRecorder.Current.Value.Count > 0:
                        {
                            await Ctrl_Z_Click().ConfigureAwait(false);
                            break;
                        }
                }
            }
        }

        private void FileCollection_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                HasFile.Visibility = FileCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 关闭右键菜单
        /// </summary>
        private void Restore()
        {
            FileFlyout.Hide();
            FolderFlyout.Hide();
            EmptyFlyout.Hide();
            MixedFlyout.Hide();
        }

        private async Task Ctrl_Z_Click()
        {
            if (OperationRecorder.Current.Value.Count > 0)
            {
                await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Undoing")).ConfigureAwait(true);

                try
                {
                    foreach (string Action in OperationRecorder.Current.Value.Pop())
                    {
                        string[] SplitGroup = Action.Split("||", StringSplitOptions.RemoveEmptyEntries);

                        switch (SplitGroup[1])
                        {
                            case "Move":
                                {
                                    if (FileControlInstance.CurrentFolder.Path == Path.GetDirectoryName(SplitGroup[3]))
                                    {
                                        StorageFolder OriginFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(SplitGroup[0]));

                                        switch (SplitGroup[2])
                                        {
                                            case "File":
                                                {
                                                    if ((await FileControlInstance.CurrentFolder.TryGetItemAsync(Path.GetFileName(SplitGroup[3]))) is StorageFile File)
                                                    {
                                                        await FullTrustExcutorController.Current.MoveAsync(File, OriginFolder, (s, arg) =>
                                                        {
                                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                        }, true).ConfigureAwait(true);
                                                    }
                                                    else
                                                    {
                                                        throw new FileNotFoundException();
                                                    }

                                                    break;
                                                }
                                            case "Folder":
                                                {
                                                    if ((await FileControlInstance.CurrentFolder.TryGetItemAsync(Path.GetFileName(SplitGroup[3]))) is StorageFolder Folder)
                                                    {
                                                        await FullTrustExcutorController.Current.MoveAsync(Folder, OriginFolder, (s, arg) =>
                                                        {
                                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                        }, true).ConfigureAwait(true);
                                                    }
                                                    else
                                                    {
                                                        throw new FileNotFoundException();
                                                    }

                                                    break;
                                                }
                                        }
                                    }
                                    else if (FileControlInstance.CurrentFolder.Path == Path.GetDirectoryName(SplitGroup[0]))
                                    {
                                        switch (SplitGroup[2])
                                        {
                                            case "File":
                                                {
                                                    StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(SplitGroup[3]));

                                                    if ((await TargetFolder.TryGetItemAsync(Path.GetFileName(SplitGroup[3]))) is StorageFile File)
                                                    {
                                                        await FullTrustExcutorController.Current.MoveAsync(File, FileControlInstance.CurrentFolder, (s, arg) =>
                                                        {
                                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                        }, true).ConfigureAwait(true);
                                                    }
                                                    else
                                                    {
                                                        throw new FileNotFoundException();
                                                    }

                                                    break;
                                                }
                                            case "Folder":
                                                {
                                                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(SplitGroup[3]);

                                                    await FullTrustExcutorController.Current.MoveAsync(Folder, FileControlInstance.CurrentFolder, (s, arg) =>
                                                    {
                                                        FileControlInstance.ProBar.IsIndeterminate = false;
                                                        FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                    }, true).ConfigureAwait(true);

                                                    break;
                                                }
                                        }
                                    }
                                    else
                                    {
                                        StorageFolder OriginFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(SplitGroup[0]));

                                        switch (SplitGroup[2])
                                        {
                                            case "File":
                                                {
                                                    StorageFile File = await StorageFile.GetFileFromPathAsync(SplitGroup[3]);

                                                    await FullTrustExcutorController.Current.MoveAsync(File, OriginFolder, (s, arg) =>
                                                    {
                                                        FileControlInstance.ProBar.IsIndeterminate = false;
                                                        FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                    }, true).ConfigureAwait(true);

                                                    break;
                                                }
                                            case "Folder":
                                                {
                                                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(SplitGroup[3]);

                                                    await FullTrustExcutorController.Current.MoveAsync(Folder, OriginFolder, (s, arg) =>
                                                    {
                                                        FileControlInstance.ProBar.IsIndeterminate = false;
                                                        FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                    }, true).ConfigureAwait(true);

                                                    if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                    {
                                                        await FileControlInstance.FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
                                                    }

                                                    break;
                                                }
                                        }
                                    }

                                    break;
                                }
                            case "Copy":
                                {
                                    if (FileControlInstance.CurrentFolder.Path == Path.GetDirectoryName(SplitGroup[3]))
                                    {
                                        switch (SplitGroup[2])
                                        {
                                            case "File":
                                                {
                                                    if ((await FileControlInstance.CurrentFolder.TryGetItemAsync(Path.GetFileName(SplitGroup[3]))) is StorageFile File)
                                                    {
                                                        await FullTrustExcutorController.Current.DeleteAsync(File, true, (s, arg) =>
                                                        {
                                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                        }, true).ConfigureAwait(true);
                                                    }
                                                    else
                                                    {
                                                        throw new FileNotFoundException();
                                                    }

                                                    break;
                                                }
                                            case "Folder":
                                                {
                                                    if ((await FileControlInstance.CurrentFolder.TryGetItemAsync(Path.GetFileName(SplitGroup[3]))) is StorageFolder Folder)
                                                    {
                                                        await FullTrustExcutorController.Current.DeleteAsync(Folder, true, (s, arg) =>
                                                        {
                                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                        }, true).ConfigureAwait(true);
                                                    }

                                                    break;
                                                }
                                        }
                                    }
                                    else
                                    {
                                        await FullTrustExcutorController.Current.DeleteAsync(SplitGroup[3], true, (s, arg) =>
                                        {
                                            FileControlInstance.ProBar.IsIndeterminate = false;
                                            FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                        }, true).ConfigureAwait(true);

                                        if (!SettingControl.IsDetachTreeViewAndPresenter)
                                        {
                                            await FileControlInstance.FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
                                        }
                                    }
                                    break;
                                }
                            case "Delete":
                                {
                                    if ((await FullTrustExcutorController.Current.GetRecycleBinItemsAsync().ConfigureAwait(true)).FirstOrDefault((Item) => Item.RecycleItemOriginPath == SplitGroup[0]) is FileSystemStorageItem Item)
                                    {
                                        if (!await FullTrustExcutorController.Current.RestoreItemInRecycleBinAsync(Item.Path).ConfigureAwait(true))
                                        {
                                            QueueContentDialog Dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = $"{Globalization.GetString("QueueDialog_RecycleBinRestoreError_Content")} {Environment.NewLine}{Item.Name}",
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };
                                            _ = Dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                    }
                                    else
                                    {
                                        QueueContentDialog Dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_WarningTitle"),
                                            Content = Globalization.GetString("QueueDialog_UndoFailure_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };
                                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    break;
                                }
                        }
                    }
                }
                catch
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_WarningTitle"),
                        Content = Globalization.GetString("QueueDialog_UndoFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }

                await FileControlInstance.LoadingActivation(false).ConfigureAwait(false);
            }
        }

        public async void Copy_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItems.Any((Item) => Item.IsHidenItem))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(false);
                return;
            }

            Clipboard.Clear();

            List<IStorageItem> TempList = new List<IStorageItem>(SelectedItems.Count);

            foreach (var Item in SelectedItems)
            {
                if (await Item.GetStorageItem().ConfigureAwait(true) is IStorageItem It)
                {
                    TempList.Add(It);
                }
            }

            if (TempList.Count > 0)
            {
                DataPackage Package = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Copy
                };
                Package.SetStorageItems(TempList, false);

                Clipboard.SetContent(Package);
            }

            FileCollection.Where((Item) => Item.ThumbnailOpacity != 1d).ToList().ForEach((Item) => Item.SetThumbnailOpacity(ThumbnailStatus.Normal));
        }

        public async void Paste_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            try
            {
                DataPackageView Package = Clipboard.GetContent();

                if (Package.Contains(StandardDataFormats.StorageItems))
                {
                    IReadOnlyList<IStorageItem> ItemList = await Package.GetStorageItemsAsync();

                    if (Package.RequestedOperation.HasFlag(DataPackageOperation.Move))
                    {
                        if (ItemList.Select((Item) => Item.Path).All((Item) => Path.GetDirectoryName(Item) == FileControlInstance.CurrentFolder.Path))
                        {
                            return;
                        }

                        await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                        bool IsItemNotFound = false;
                        bool IsUnauthorized = false;
                        bool IsCaptured = false;
                        bool IsOperateFailed = false;

                        try
                        {
                            if (FileControlInstance.IsNetworkDevice)
                            {
                                foreach (IStorageItem NewItem in ItemList)
                                {
                                    if (NewItem is StorageFile File)
                                    {
                                        await File.MoveAsync(FileControlInstance.CurrentFolder, File.Name, NameCollisionOption.GenerateUniqueName);

                                        FileCollection.Add(new FileSystemStorageItem(File, await File.GetSizeRawDataAsync().ConfigureAwait(true), await File.GetThumbnailBitmapAsync().ConfigureAwait(true), await File.GetModifiedTimeAsync().ConfigureAwait(true)));
                                    }
                                    else if (NewItem is StorageFolder Folder)
                                    {
                                        StorageFolder NewFolder = await FileControlInstance.CurrentFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);

                                        await Folder.MoveSubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                        FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));

                                        if (!SettingControl.IsDetachTreeViewAndPresenter)
                                        {
                                            FileControlInstance.CurrentNode.HasUnrealizedChildren = true;

                                            if (FileControlInstance.CurrentNode.IsExpanded)
                                            {
                                                FileControlInstance.CurrentNode.Children.Add(new TreeViewNode
                                                {
                                                    Content = new TreeViewNodeContent(NewFolder),
                                                    HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                await FullTrustExcutorController.Current.MoveAsync(ItemList, FileControlInstance.CurrentFolder, (s, arg) =>
                                {
                                    FileControlInstance.ProBar.IsIndeterminate = false;
                                    FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                }).ConfigureAwait(true);
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            IsItemNotFound = true;
                        }
                        catch (FileCaputureException)
                        {
                            IsCaptured = true;
                        }
                        catch (InvalidOperationException)
                        {
                            IsOperateFailed = true;
                        }
                        catch (Exception)
                        {
                            IsUnauthorized = true;
                        }

                        if (IsItemNotFound)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_MoveFailForNotExist_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                        else if (IsUnauthorized)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                            };

                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                            }
                        }
                        else if (IsCaptured)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                        else if (IsOperateFailed)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_MoveFailUnexpectError_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                    else if (Package.RequestedOperation.HasFlag(DataPackageOperation.Copy))
                    {
                        await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                        bool IsItemNotFound = false;
                        bool IsUnauthorized = false;
                        bool IsOperateFailed = false;

                        try
                        {
                            if (FileControlInstance.IsNetworkDevice)
                            {
                                foreach (IStorageItem NewItem in ItemList)
                                {
                                    if (NewItem is StorageFile File)
                                    {
                                        StorageFile NewFile = await File.CopyAsync(FileControlInstance.CurrentFolder, File.Name, NameCollisionOption.GenerateUniqueName);

                                        FileCollection.Add(new FileSystemStorageItem(NewFile, await NewFile.GetSizeRawDataAsync().ConfigureAwait(true), await NewFile.GetThumbnailBitmapAsync().ConfigureAwait(true), await NewFile.GetModifiedTimeAsync().ConfigureAwait(true)));
                                    }
                                    else if (NewItem is StorageFolder Folder)
                                    {
                                        StorageFolder NewFolder = await FileControlInstance.CurrentFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);

                                        await Folder.CopySubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                        FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));

                                        if (!SettingControl.IsDetachTreeViewAndPresenter)
                                        {
                                            FileControlInstance.CurrentNode.HasUnrealizedChildren = true;

                                            if (FileControlInstance.CurrentNode.IsExpanded)
                                            {
                                                FileControlInstance.CurrentNode.Children.Add(new TreeViewNode
                                                {
                                                    Content = new TreeViewNodeContent(NewFolder),
                                                    HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                await FullTrustExcutorController.Current.CopyAsync(ItemList, FileControlInstance.CurrentFolder, (s, arg) =>
                                {
                                    FileControlInstance.ProBar.IsIndeterminate = false;
                                    FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                }).ConfigureAwait(true);
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            IsItemNotFound = true;
                        }
                        catch (InvalidOperationException)
                        {
                            IsOperateFailed = true;
                        }
                        catch (Exception)
                        {
                            IsUnauthorized = true;
                        }

                        if (IsItemNotFound)
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_CopyFailForNotExist_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                        }
                        else if (IsUnauthorized)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                            };

                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                            }
                        }
                        else if (IsOperateFailed)
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_CopyFailUnexpectError_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }
            }
            catch
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_FailToGetClipboardError_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);
            }
            finally
            {
                await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
                FileCollection.Where((Item) => Item.ThumbnailOpacity != 1d).ToList().ForEach((Item) => Item.SetThumbnailOpacity(ThumbnailStatus.Normal));
            }
        }

        public async void Cut_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItems.Any((Item) => Item.IsHidenItem))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(false);
                return;
            }

            Clipboard.Clear();

            List<IStorageItem> TempList = new List<IStorageItem>(SelectedItems.Count);
            foreach (var Item in SelectedItems)
            {
                if (await Item.GetStorageItem().ConfigureAwait(true) is IStorageItem It)
                {
                    TempList.Add(It);
                }
            }

            if (TempList.Count > 0)
            {
                DataPackage Package = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Move
                };
                Package.SetStorageItems(TempList, false);

                Clipboard.SetContent(Package);
            }

            FileCollection.Where((Item) => Item.ThumbnailOpacity != 1d).ToList().ForEach((Item) => Item.SetThumbnailOpacity(ThumbnailStatus.Normal));
            SelectedItems.ForEach((Item) => Item.SetThumbnailOpacity(ThumbnailStatus.ReduceOpacity));
        }

        public async void Delete_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            DeleteDialog QueueContenDialog = new DeleteDialog(Globalization.GetString("QueueDialog_DeleteFiles_Content"));

            if ((await QueueContenDialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
            {
                await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Deleting")).ConfigureAwait(true);

                bool IsItemNotFound = false;
                bool IsUnauthorized = false;
                bool IsCaptured = false;
                bool IsOperateFailed = false;

                try
                {
                    List<string> PathList = SelectedItems.Select((Item) => Item.Path).ToList();

                    await FullTrustExcutorController.Current.DeleteAsync(PathList, QueueContenDialog.IsPermanentDelete, (s, arg) =>
                    {
                        FileControlInstance.ProBar.IsIndeterminate = false;
                        FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                    }).ConfigureAwait(true);

                    if (FileControlInstance.IsNetworkDevice)
                    {
                        foreach (FileSystemStorageItem Item in FileCollection.Where((Item) => PathList.Contains(Item.Path)).ToList())
                        {
                            FileCollection.Remove(Item);

                            if (!SettingControl.IsDetachTreeViewAndPresenter)
                            {
                                if (FileControlInstance.CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == Item.Path) is TreeViewNode Node)
                                {
                                    FileControlInstance.CurrentNode.Children.Remove(Node);
                                }
                            }
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    IsItemNotFound = true;
                }
                catch (FileCaputureException)
                {
                    IsCaptured = true;
                }
                catch (InvalidOperationException)
                {
                    IsOperateFailed = true;
                }
                catch (Exception)
                {
                    IsUnauthorized = true;
                }

                if (IsItemNotFound)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(true);
                }
                else if (IsUnauthorized)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedDelete_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                    }
                }
                else if (IsCaptured)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else if (IsOperateFailed)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteFailUnexpectError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }

                await FileControlInstance.LoadingActivation(false).ConfigureAwait(false);
            }
        }

        public async void Rename_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItems.Count > 1)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_RenameNumError_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            if (SelectedItem is FileSystemStorageItem RenameItem)
            {
                if ((await RenameItem.GetStorageItem().ConfigureAwait(true)) is StorageFile File)
                {
                    if (!await File.CheckExist().ConfigureAwait(true))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);

                        await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                        return;
                    }

                    RenameDialog dialog = new RenameDialog(File);
                    if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                    {
                        try
                        {
                            await File.RenameAsync(dialog.DesireName);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_UnauthorizedRenameFile_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                            };

                            if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                            }
                        }
                        catch (FileLoadException)
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_FileOccupied_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                        }
                        catch
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_RenameExist_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                            };

                            if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                await (await RenameItem.GetStorageItem().ConfigureAwait(true)).RenameAsync(dialog.DesireName, NameCollisionOption.GenerateUniqueName);
                            }
                        }

                    }
                }
                else if ((await RenameItem.GetStorageItem().ConfigureAwait(true)) is StorageFolder Folder)
                {
                    if (!await Folder.CheckExist().ConfigureAwait(true))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);

                        await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                        return;
                    }

                    RenameDialog dialog = new RenameDialog(Folder);
                    if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                    {
                        if (string.IsNullOrWhiteSpace(dialog.DesireName))
                        {
                            QueueContentDialog content = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_EmptyFolderName_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };
                            await content.ShowAsync().ConfigureAwait(true);

                            return;
                        }

                        try
                        {
                            string OldPath = Folder.Path;

                            await Folder.RenameAsync(dialog.DesireName);

                            await RenameItem.Replace(Folder.Path).ConfigureAwait(true);

                            if (!SettingControl.IsDetachTreeViewAndPresenter && FileControlInstance.IsNetworkDevice && FileControlInstance.CurrentNode.Children.Select((Item) => Item.Content as TreeViewNodeContent).FirstOrDefault((Item) => Item.Path == OldPath) is TreeViewNodeContent Content)
                            {
                                Content.Update(Folder);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_UnauthorizedRenameFolder_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                            };

                            if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                            }
                        }
                        catch (FileLoadException)
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_FolderOccupied_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };

                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                        }
                        catch
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_RenameExist_Content"),
                                PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                            };

                            if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                string OldPath = Folder.Path;

                                await Folder.RenameAsync(dialog.DesireName, NameCollisionOption.GenerateUniqueName);

                                await RenameItem.Replace(Folder.Path).ConfigureAwait(true);

                                if (!SettingControl.IsDetachTreeViewAndPresenter && FileControlInstance.IsNetworkDevice && FileControlInstance.CurrentNode.Children.Select((Item) => Item.Content as TreeViewNodeContent).FirstOrDefault((Item) => Item.Path == Folder.Path) is TreeViewNodeContent Content)
                                {
                                    Content.Update(Folder);
                                }
                            }
                        }
                    }
                }
                else
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedRenameFolder_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                    }
                }
            }
        }

        public async void BluetoothShare_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            StorageFile ShareFile = (await SelectedItem.GetStorageItem().ConfigureAwait(true)) as StorageFile;

            if (!await ShareFile.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                return;
            }

            IReadOnlyList<Radio> RadioDevice = await Radio.GetRadiosAsync();

            if (RadioDevice.Any((Device) => Device.Kind == RadioKind.Bluetooth && Device.State == RadioState.On))
            {
                BluetoothUI Bluetooth = new BluetoothUI();
                if ((await Bluetooth.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                {
                    BluetoothFileTransfer FileTransfer = new BluetoothFileTransfer(ShareFile);

                    _ = await FileTransfer.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                    Content = Globalization.GetString("QueueDialog_OpenBluetooth_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);
            }
        }

        private void ViewControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MixZip.IsEnabled = true;

            if (SelectedItems.Any((Item) => Item.StorageType != StorageItemTypes.Folder))
            {
                if (SelectedItems.All((Item) => Item.StorageType == StorageItemTypes.File))
                {
                    if (SelectedItems.All((Item) => Item.Type == ".zip"))
                    {
                        MixZip.Label = Globalization.GetString("Operate_Text_Decompression");
                    }
                    else if (SelectedItems.All((Item) => Item.Type != ".zip"))
                    {
                        MixZip.Label = Globalization.GetString("Operate_Text_Compression");
                    }
                    else
                    {
                        MixZip.IsEnabled = false;
                    }
                }
                else
                {
                    if (SelectedItems.Where((It) => It.StorageType == StorageItemTypes.File).Any((Item) => Item.Type == ".zip"))
                    {
                        MixZip.IsEnabled = false;
                    }
                    else
                    {
                        MixZip.Label = Globalization.GetString("Operate_Text_Compression");
                    }
                }
            }
            else
            {
                MixZip.Label = Globalization.GetString("Operate_Text_Compression");
            }

            if (SelectedItem is FileSystemStorageItem Item)
            {
                if (Item.StorageType == StorageItemTypes.File)
                {
                    Transcode.IsEnabled = false;
                    VideoEdit.IsEnabled = false;
                    VideoMerge.IsEnabled = false;
                    ChooseOtherApp.IsEnabled = true;
                    RunWithSystemAuthority.IsEnabled = false;

                    Zip.Label = Globalization.GetString("Operate_Text_Compression");

                    switch (Item.Type)
                    {
                        case ".zip":
                            {
                                Zip.Label = Globalization.GetString("Operate_Text_Decompression");
                                break;
                            }
                        case ".mp4":
                        case ".wmv":
                            {
                                VideoEdit.IsEnabled = true;
                                Transcode.IsEnabled = true;
                                VideoMerge.IsEnabled = true;
                                break;
                            }
                        case ".mkv":
                        case ".m4a":
                        case ".mov":
                            {
                                Transcode.IsEnabled = true;
                                break;
                            }
                        case ".mp3":
                        case ".flac":
                        case ".wma":
                        case ".alac":
                        case ".png":
                        case ".bmp":
                        case ".jpg":
                        case ".heic":
                        case ".gif":
                        case ".tiff":
                            {
                                Transcode.IsEnabled = true;
                                break;
                            }
                        case ".exe":
                        case ".bat":
                            {
                                ChooseOtherApp.IsEnabled = false;
                                RunWithSystemAuthority.IsEnabled = true;
                                break;
                            }
                    }
                }
            }
        }

        private void ViewControl_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext == null)
            {
                SelectedItem = null;
                FileControlInstance.IsSearchOrPathBoxFocused = false;
            }
        }

        private void ViewControl_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType == PointerDeviceType.Mouse)
            {
                if (ItemPresenter is GridView)
                {
                    if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Context)
                    {
                        if (SelectedItems.Count <= 1 || !SelectedItems.Contains(Context))
                        {
                            if (Context.IsHidenItem)
                            {
                                ItemPresenter.ContextFlyout = HiddenItemFlyout;
                            }
                            else
                            {
                                ItemPresenter.ContextFlyout = Context.StorageType == StorageItemTypes.Folder ? FolderFlyout : FileFlyout;
                            }

                            SelectedItem = Context;
                        }
                        else
                        {
                            if (SelectedItems.All((Item) => !Item.IsHidenItem))
                            {
                                ItemPresenter.ContextFlyout = MixedFlyout;
                            }
                            else
                            {
                                ItemPresenter.ContextFlyout = null;
                            }
                        }
                    }
                    else
                    {
                        SelectedItem = null;
                        ItemPresenter.ContextFlyout = EmptyFlyout;
                    }
                }
                else
                {
                    if (e.OriginalSource is ListViewItemPresenter || (e.OriginalSource as FrameworkElement)?.Name == "EmptyTextblock")
                    {
                        SelectedItem = null;
                        ItemPresenter.ContextFlyout = EmptyFlyout;
                    }
                    else
                    {
                        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Context)
                        {
                            if (SelectedItems.Count <= 1 || !SelectedItems.Contains(Context))
                            {
                                if (Context.IsHidenItem)
                                {
                                    ItemPresenter.ContextFlyout = HiddenItemFlyout;
                                }
                                else
                                {
                                    ItemPresenter.ContextFlyout = Context.StorageType == StorageItemTypes.Folder ? FolderFlyout : FileFlyout;
                                }

                                SelectedItem = Context;
                            }
                            else
                            {
                                if (SelectedItems.All((Item) => !Item.IsHidenItem))
                                {
                                    ItemPresenter.ContextFlyout = MixedFlyout;
                                }
                                else
                                {
                                    ItemPresenter.ContextFlyout = null;
                                }
                            }
                        }
                        else
                        {
                            SelectedItem = null;
                            ItemPresenter.ContextFlyout = EmptyFlyout;
                        }
                    }
                }
            }

            e.Handled = true;
        }

        public async void Attribute_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            StorageFile Device = (await SelectedItem.GetStorageItem().ConfigureAwait(true)) as StorageFile;

            if (!await Device.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                return;
            }

            PropertyDialog Dialog = new PropertyDialog(Device);
            _ = await Dialog.ShowAsync().ConfigureAwait(true);
        }

        public async void Zip_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            StorageFile Item = (await SelectedItem.GetStorageItem().ConfigureAwait(true)) as StorageFile;

            if (!await Item.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                return;
            }

            if (Item.FileType == ".zip")
            {
                await UnZipAsync(Item).ConfigureAwait(true);
            }
            else
            {
                ZipDialog dialog = new ZipDialog(true, Item.DisplayName);

                if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                {
                    await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Compressing")).ConfigureAwait(true);

                    if (dialog.IsCryptionEnable)
                    {
                        await CreateZipAsync(Item, dialog.FileName, (int)dialog.Level, true, dialog.Key, dialog.Password).ConfigureAwait(true);
                    }
                    else
                    {
                        await CreateZipAsync(Item, dialog.FileName, (int)dialog.Level).ConfigureAwait(true);
                    }

                    await FileControlInstance.LoadingActivation(false).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 执行ZIP解压功能
        /// </summary>
        /// <param name="ZFileList">ZIP文件</param>
        /// <returns>无</returns>
        private async Task<StorageFolder> UnZipAsync(StorageFile ZFile)
        {
            StorageFolder NewFolder = null;
            using (Stream ZipFileStream = await ZFile.OpenStreamForReadAsync().ConfigureAwait(true))
            using (ZipFile ZipEntries = new ZipFile(ZipFileStream))
            {
                ZipEntries.IsStreamOwner = false;

                if (ZipEntries.Count == 0)
                {
                    return null;
                }

                try
                {
                    if (ZipEntries[0].IsCrypted)
                    {
                        ZipDialog Dialog = new ZipDialog(false);
                        if ((await Dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                        {
                            await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Extracting")).ConfigureAwait(true);
                            ZipEntries.Password = Dialog.Password;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Extracting")).ConfigureAwait(true);
                    }

                    NewFolder = await FileControlInstance.CurrentFolder.CreateFolderAsync(Path.GetFileNameWithoutExtension(ZFile.Name), CreationCollisionOption.OpenIfExists);

                    foreach (ZipEntry Entry in ZipEntries)
                    {
                        using (Stream ZipEntryStream = ZipEntries.GetInputStream(Entry))
                        {
                            StorageFile NewFile = null;

                            if (Entry.Name.Contains("/"))
                            {
                                string[] SplitFolderPath = Entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                                StorageFolder TempFolder = NewFolder;
                                for (int i = 0; i < SplitFolderPath.Length - 1; i++)
                                {
                                    TempFolder = await TempFolder.CreateFolderAsync(SplitFolderPath[i], CreationCollisionOption.OpenIfExists);
                                }

                                if (Entry.Name.Last() == '/')
                                {
                                    await TempFolder.CreateFolderAsync(SplitFolderPath.Last(), CreationCollisionOption.OpenIfExists);
                                    continue;
                                }
                                else
                                {
                                    NewFile = await TempFolder.CreateFileAsync(SplitFolderPath.Last(), CreationCollisionOption.ReplaceExisting);
                                }
                            }
                            else
                            {
                                NewFile = await NewFolder.CreateFileAsync(Entry.Name, CreationCollisionOption.ReplaceExisting);
                            }

                            using (Stream NewFileStream = await NewFile.OpenStreamForWriteAsync().ConfigureAwait(true))
                            {
                                await ZipEntryStream.CopyToAsync(NewFileStream).ConfigureAwait(true);
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedDecompression_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                    }
                }
                catch (Exception e)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DecompressionError_Content") + e.Message,
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                finally
                {
                    await FileControlInstance.LoadingActivation(false).ConfigureAwait(false);
                }
            }

            return NewFolder;
        }

        /// <summary>
        /// 执行ZIP文件创建功能
        /// </summary>
        /// <param name="FileList">待压缩文件</param>
        /// <param name="NewZipName">生成的Zip文件名</param>
        /// <param name="ZipLevel">压缩等级</param>
        /// <param name="EnableCryption">是否启用加密</param>
        /// <param name="Size">AES加密密钥长度</param>
        /// <param name="Password">密码</param>
        /// <returns>无</returns>
        private async Task CreateZipAsync(IStorageItem ZipTarget, string NewZipName, int ZipLevel, bool EnableCryption = false, KeySize Size = KeySize.None, string Password = null)
        {
            try
            {
                StorageFile Newfile = await FileControlInstance.CurrentFolder.CreateFileAsync(NewZipName, CreationCollisionOption.GenerateUniqueName);

                using (Stream NewFileStream = await Newfile.OpenStreamForWriteAsync().ConfigureAwait(true))
                using (ZipOutputStream OutputStream = new ZipOutputStream(NewFileStream))
                {
                    OutputStream.IsStreamOwner = false;
                    OutputStream.SetLevel(ZipLevel);
                    OutputStream.UseZip64 = UseZip64.Dynamic;

                    if (EnableCryption)
                    {
                        OutputStream.Password = Password;
                    }

                    try
                    {
                        if (ZipTarget is StorageFile ZipFile)
                        {
                            if (EnableCryption)
                            {
                                using (Stream FileStream = await ZipFile.OpenStreamForReadAsync().ConfigureAwait(true))
                                {
                                    ZipEntry NewEntry = new ZipEntry(ZipFile.Name)
                                    {
                                        DateTime = DateTime.Now,
                                        AESKeySize = (int)Size,
                                        IsCrypted = true,
                                        CompressionMethod = CompressionMethod.Deflated,
                                        Size = FileStream.Length
                                    };

                                    OutputStream.PutNextEntry(NewEntry);

                                    await FileStream.CopyToAsync(OutputStream).ConfigureAwait(true);
                                }
                            }
                            else
                            {
                                using (Stream FileStream = await ZipFile.OpenStreamForReadAsync().ConfigureAwait(true))
                                {
                                    ZipEntry NewEntry = new ZipEntry(ZipFile.Name)
                                    {
                                        DateTime = DateTime.Now,
                                        CompressionMethod = CompressionMethod.Deflated,
                                        Size = FileStream.Length
                                    };

                                    OutputStream.PutNextEntry(NewEntry);

                                    await FileStream.CopyToAsync(OutputStream).ConfigureAwait(true);
                                }
                            }
                        }
                        else if (ZipTarget is StorageFolder ZipFolder)
                        {
                            await ZipFolderCore(ZipFolder, OutputStream, ZipFolder.Name, EnableCryption, Size, Password).ConfigureAwait(true);
                        }

                        await OutputStream.FlushAsync().ConfigureAwait(true);
                        OutputStream.Finish();
                    }
                    catch (Exception e)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_CompressionError_Content") + e.Message,
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_UnauthorizedCompression_Content"),
                    PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                };

                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                }
            }
        }

        /// <summary>
        /// 执行ZIP文件创建功能
        /// </summary>
        /// <param name="FileList">待压缩文件</param>
        /// <param name="NewZipName">生成的Zip文件名</param>
        /// <param name="ZipLevel">压缩等级</param>
        /// <param name="EnableCryption">是否启用加密</param>
        /// <param name="Size">AES加密密钥长度</param>
        /// <param name="Password">密码</param>
        /// <returns>无</returns>
        private async Task CreateZipAsync(IEnumerable<FileSystemStorageItem> ZipItemGroup, string NewZipName, int ZipLevel, bool EnableCryption = false, KeySize Size = KeySize.None, string Password = null)
        {
            try
            {
                StorageFile Newfile = await FileControlInstance.CurrentFolder.CreateFileAsync(NewZipName, CreationCollisionOption.GenerateUniqueName);

                using (Stream NewFileStream = await Newfile.OpenStreamForWriteAsync().ConfigureAwait(true))
                using (ZipOutputStream OutputStream = new ZipOutputStream(NewFileStream))
                {
                    OutputStream.IsStreamOwner = false;
                    OutputStream.SetLevel(ZipLevel);
                    OutputStream.UseZip64 = UseZip64.Dynamic;

                    if (EnableCryption)
                    {
                        OutputStream.Password = Password;
                    }

                    try
                    {
                        foreach (FileSystemStorageItem StorageItem in ZipItemGroup)
                        {
                            if (await StorageItem.GetStorageItem().ConfigureAwait(true) is StorageFile ZipFile)
                            {
                                if (EnableCryption)
                                {
                                    using (Stream FileStream = await ZipFile.OpenStreamForReadAsync().ConfigureAwait(true))
                                    {
                                        ZipEntry NewEntry = new ZipEntry(ZipFile.Name)
                                        {
                                            DateTime = DateTime.Now,
                                            AESKeySize = (int)Size,
                                            IsCrypted = true,
                                            CompressionMethod = CompressionMethod.Deflated,
                                            Size = FileStream.Length
                                        };

                                        OutputStream.PutNextEntry(NewEntry);

                                        await FileStream.CopyToAsync(OutputStream).ConfigureAwait(true);
                                    }
                                }
                                else
                                {
                                    using (Stream FileStream = await ZipFile.OpenStreamForReadAsync().ConfigureAwait(true))
                                    {
                                        ZipEntry NewEntry = new ZipEntry(ZipFile.Name)
                                        {
                                            DateTime = DateTime.Now,
                                            CompressionMethod = CompressionMethod.Deflated,
                                            Size = FileStream.Length
                                        };

                                        OutputStream.PutNextEntry(NewEntry);

                                        await FileStream.CopyToAsync(OutputStream).ConfigureAwait(true);
                                    }
                                }
                            }
                            else if (await StorageItem.GetStorageItem().ConfigureAwait(true) is StorageFolder ZipFolder)
                            {
                                await ZipFolderCore(ZipFolder, OutputStream, ZipFolder.Name, EnableCryption, Size, Password).ConfigureAwait(true);
                            }
                        }

                        await OutputStream.FlushAsync().ConfigureAwait(true);
                        OutputStream.Finish();
                    }
                    catch (Exception e)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_CompressionError_Content") + e.Message,
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_UnauthorizedCompression_Content"),
                    PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                };

                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                }
            }
        }

        private async Task ZipFolderCore(StorageFolder Folder, ZipOutputStream OutputStream, string BaseFolderName, bool EnableCryption = false, KeySize Size = KeySize.None, string Password = null)
        {
            IReadOnlyList<IStorageItem> ItemsCollection = await Folder.GetItemsAsync();

            if (ItemsCollection.Count == 0)
            {
                if (!string.IsNullOrEmpty(BaseFolderName))
                {
                    ZipEntry NewEntry = new ZipEntry(BaseFolderName);
                    OutputStream.PutNextEntry(NewEntry);
                    OutputStream.CloseEntry();
                }
            }
            else
            {
                foreach (IStorageItem Item in ItemsCollection)
                {
                    if (Item is StorageFolder InnerFolder)
                    {
                        if (EnableCryption)
                        {
                            await ZipFolderCore(InnerFolder, OutputStream, $"{BaseFolderName}/{InnerFolder.Name}", true, Size, Password).ConfigureAwait(false);
                        }
                        else
                        {
                            await ZipFolderCore(InnerFolder, OutputStream, $"{BaseFolderName}/{InnerFolder.Name}").ConfigureAwait(false);
                        }
                    }
                    else if (Item is StorageFile InnerFile)
                    {
                        if (EnableCryption)
                        {
                            using (Stream FileStream = await InnerFile.OpenStreamForReadAsync().ConfigureAwait(true))
                            {
                                ZipEntry NewEntry = new ZipEntry($"{BaseFolderName}/{InnerFile.Name}")
                                {
                                    DateTime = DateTime.Now,
                                    AESKeySize = (int)Size,
                                    IsCrypted = true,
                                    CompressionMethod = CompressionMethod.Deflated,
                                    Size = FileStream.Length
                                };

                                OutputStream.PutNextEntry(NewEntry);

                                await FileStream.CopyToAsync(OutputStream).ConfigureAwait(false);

                                OutputStream.CloseEntry();
                            }
                        }
                        else
                        {
                            using (Stream FileStream = await InnerFile.OpenStreamForReadAsync().ConfigureAwait(true))
                            {
                                ZipEntry NewEntry = new ZipEntry($"{BaseFolderName}/{InnerFile.Name}")
                                {
                                    DateTime = DateTime.Now,
                                    CompressionMethod = CompressionMethod.Deflated,
                                    Size = FileStream.Length
                                };

                                OutputStream.PutNextEntry(NewEntry);

                                await FileStream.CopyToAsync(OutputStream).ConfigureAwait(false);

                                OutputStream.CloseEntry();
                            }
                        }
                    }
                }
            }
        }

        private async void ViewControl_DoubleTapped(object sender, Windows.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (SettingControl.IsInputFromPrimaryButton && (e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem ReFile)
            {
                await EnterSelectedItem(ReFile).ConfigureAwait(false);
            }
        }

        public async void Transcode_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile Source)
            {
                if (!await Source.CheckExist().ConfigureAwait(true))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                    return;
                }

                if (GeneralTransformer.IsAnyTransformTaskRunning)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_TipTitle"),
                        Content = Globalization.GetString("QueueDialog_TaskWorking_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    return;
                }

                switch (Source.FileType)
                {
                    case ".mkv":
                    case ".mp4":
                    case ".mp3":
                    case ".flac":
                    case ".wma":
                    case ".wmv":
                    case ".m4a":
                    case ".mov":
                    case ".alac":
                        {
                            TranscodeDialog dialog = new TranscodeDialog(Source);

                            if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                            {
                                try
                                {
                                    StorageFile DestinationFile = await FileControlInstance.CurrentFolder.CreateFileAsync(Source.DisplayName + "." + dialog.MediaTranscodeEncodingProfile.ToLower(), CreationCollisionOption.GenerateUniqueName);

                                    await GeneralTransformer.TranscodeFromAudioOrVideoAsync(Source, DestinationFile, dialog.MediaTranscodeEncodingProfile, dialog.MediaTranscodeQuality, dialog.SpeedUp).ConfigureAwait(true);
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    QueueContentDialog Dialog = new QueueContentDialog
                                    {
                                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                        Content = Globalization.GetString("QueueDialog_UnauthorizedCreateNewFile_Content"),
                                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                                    };

                                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                    {
                                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                                    }
                                }
                            }

                            break;
                        }
                    case ".png":
                    case ".bmp":
                    case ".jpg":
                    case ".heic":
                    case ".tiff":
                        {
                            TranscodeImageDialog Dialog = null;
                            using (IRandomAccessStream OriginStream = await Source.OpenAsync(FileAccessMode.Read))
                            {
                                BitmapDecoder Decoder = await BitmapDecoder.CreateAsync(OriginStream);
                                Dialog = new TranscodeImageDialog(Decoder.PixelWidth, Decoder.PixelHeight);
                            }

                            if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                            {
                                await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Transcoding")).ConfigureAwait(true);

                                await GeneralTransformer.TranscodeFromImageAsync(Source, Dialog.TargetFile, Dialog.IsEnableScale, Dialog.ScaleWidth, Dialog.ScaleHeight, Dialog.InterpolationMode).ConfigureAwait(true);

                                await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
                            }
                            break;
                        }
                }
            }
        }

        public async void FolderOpen_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItem is FileSystemStorageItem Item)
            {
                await EnterSelectedItem(Item).ConfigureAwait(false);
            }
        }

        public async void FolderProperty_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            StorageFolder Device = (await SelectedItem.GetStorageItem().ConfigureAwait(true)) as StorageFolder;
            if (!await Device.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                return;
            }

            PropertyDialog Dialog = new PropertyDialog(Device);
            _ = await Dialog.ShowAsync().ConfigureAwait(true);
        }

        public async void WIFIShare_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile Item)
            {
                if (QRTeachTip.IsOpen)
                {
                    QRTeachTip.IsOpen = false;
                }

                await Task.Run(() =>
                {
                    SpinWait.SpinUntil(() => WiFiProvider == null);
                }).ConfigureAwait(true);

                WiFiProvider = new WiFiShareProvider();
                WiFiProvider.ThreadExitedUnexpectly += WiFiProvider_ThreadExitedUnexpectly;

                string Hash = Item.Path.ComputeMD5Hash();
                QRText.Text = WiFiProvider.CurrentUri + Hash;
                WiFiProvider.FilePathMap = new KeyValuePair<string, string>(Hash, Item.Path);

                QrCodeEncodingOptions options = new QrCodeEncodingOptions()
                {
                    DisableECI = true,
                    CharacterSet = "UTF-8",
                    Width = 250,
                    Height = 250,
                    ErrorCorrection = ErrorCorrectionLevel.Q
                };

                BarcodeWriter Writer = new BarcodeWriter
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = options
                };

                WriteableBitmap Bitmap = Writer.Write(QRText.Text);
                using (SoftwareBitmap PreTransImage = SoftwareBitmap.CreateCopyFromBuffer(Bitmap.PixelBuffer, BitmapPixelFormat.Bgra8, 250, 250))
                using (SoftwareBitmap TransferImage = ComputerVisionProvider.ExtendImageBorder(PreTransImage, Colors.White, 0, 75, 75, 0))
                {
                    SoftwareBitmapSource Source = new SoftwareBitmapSource();
                    QRImage.Source = Source;
                    await Source.SetBitmapAsync(TransferImage);
                }

                await Task.Delay(500).ConfigureAwait(true);

                QRTeachTip.Target = ItemPresenter.ContainerFromItem(SelectedItem) as FrameworkElement;
                QRTeachTip.IsOpen = true;

                await WiFiProvider.StartToListenRequest().ConfigureAwait(false);
            }
            else
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
            }
        }

        private async void WiFiProvider_ThreadExitedUnexpectly(object sender, Exception e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                QRTeachTip.IsOpen = false;

                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_WiFiError_Content") + e.Message,
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);
            });
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            DataPackage Package = new DataPackage();
            Package.SetText(QRText.Text);
            Clipboard.SetContent(Package);
        }

        public async void UseSystemFileMananger_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
        }

        public async void ParentProperty_Click(object sender, RoutedEventArgs e)
        {
            if (!await FileControlInstance.CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            if (FileControlInstance.CurrentFolder.Path == Path.GetPathRoot(FileControlInstance.CurrentFolder.Path))
            {
                if (TabViewContainer.ThisPage.HardDeviceList.FirstOrDefault((Device) => Device.Name == FileControlInstance.CurrentFolder.DisplayName) is HardDeviceInfo Info)
                {
                    DeviceInfoDialog dialog = new DeviceInfoDialog(Info);
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else
                {
                    PropertyDialog Dialog = new PropertyDialog(FileControlInstance.CurrentFolder);
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                PropertyDialog Dialog = new PropertyDialog(FileControlInstance.CurrentFolder);
                _ = await Dialog.ShowAsync().ConfigureAwait(true);
            }
        }

        public async void FileOpen_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItem is FileSystemStorageItem ReFile)
            {
                await EnterSelectedItem(ReFile).ConfigureAwait(false);
            }
        }

        private void QRText_GotFocus(object sender, RoutedEventArgs e)
        {
            ((TextBox)sender).SelectAll();
        }

        public async void AddToLibray_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFolder folder)
            {
                if (!await folder.CheckExist().ConfigureAwait(true))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                    return;
                }

                if (TabViewContainer.ThisPage.LibraryFolderList.Any((Folder) => Folder.Folder.Path == folder.Path))
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_TipTitle"),
                        Content = Globalization.GetString("QueueDialog_RepeatAddToHomePage_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else
                {
                    TabViewContainer.ThisPage.LibraryFolderList.Add(new LibraryFolder(folder, await folder.GetThumbnailBitmapAsync().ConfigureAwait(true)));
                    await SQLite.Current.SetLibraryPathAsync(folder.Path, LibraryType.UserCustom).ConfigureAwait(false);
                }
            }
        }

        public async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (!await FileControlInstance.CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            try
            {
                StorageFolder NewFolder = await FileControlInstance.CurrentFolder.CreateFolderAsync(Globalization.GetString("Create_NewFolder_Admin_Name"), CreationCollisionOption.GenerateUniqueName);

                if (FileControlInstance.IsNetworkDevice)
                {
                    FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));

                    if (!SettingControl.IsDetachTreeViewAndPresenter && FileControlInstance.CurrentNode.IsExpanded)
                    {
                        FileControlInstance.CurrentNode.Children.Add(new TreeViewNode
                        {
                            Content = new TreeViewNodeContent(NewFolder),
                            HasUnrealizedChildren = false
                        });
                    }
                }
            }
            catch
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_UnauthorizedCreateFolder_Content"),
                    PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                };

                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                }
            }
        }

        private void EmptyFlyout_Opening(object sender, object e)
        {
            try
            {
                if (Clipboard.GetContent().Contains(StandardDataFormats.StorageItems))
                {
                    Paste.IsEnabled = true;
                }
                else
                {
                    Paste.IsEnabled = false;
                }
            }
            catch
            {
                Paste.IsEnabled = false;
            }

            if (OperationRecorder.Current.Value.Count > 0)
            {
                Undo.IsEnabled = true;
            }
            else
            {
                Undo.IsEnabled = false;
            }
        }

        public async void SystemShare_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile ShareItem)
            {
                if (!await ShareItem.CheckExist().ConfigureAwait(true))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                    return;
                }

                DataTransferManager.GetForCurrentView().DataRequested += (s, args) =>
                {
                    DataPackage Package = new DataPackage();
                    Package.Properties.Title = ShareItem.DisplayName;
                    Package.Properties.Description = ShareItem.DisplayType;
                    Package.SetStorageItems(new StorageFile[] { ShareItem });
                    args.Request.Data = Package;
                };

                DataTransferManager.ShowShareUI();
            }
        }

        public async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (!await FileControlInstance.CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
        }

        private async void ViewControl_ItemClick(object sender, ItemClickEventArgs e)
        {
            FileControlInstance.IsSearchOrPathBoxFocused = false;

            if (!SettingControl.IsDoubleClickEnable && e.ClickedItem is FileSystemStorageItem ReFile)
            {
                CoreVirtualKeyStates CtrlState = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
                CoreVirtualKeyStates ShiftState = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

                if (!CtrlState.HasFlag(CoreVirtualKeyStates.Down) && !ShiftState.HasFlag(CoreVirtualKeyStates.Down))
                {
                    await EnterSelectedItem(ReFile).ConfigureAwait(false);
                }
            }
        }

        private async Task EnterSelectedItem(FileSystemStorageItem ReFile, bool RunAsAdministrator = false)
        {
            await FileControlInstance.CancelAddItemOperation().ConfigureAwait(true);

            if (Interlocked.Exchange(ref TabTarget, ReFile) == null)
            {
                try
                {
                    if (WIN_Native_API.CheckIfHidden(ReFile.Path))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };

                        _ = await Dialog.ShowAsync().ConfigureAwait(false);

                        return;
                    }

                    if ((await TabTarget.GetStorageItem().ConfigureAwait(true)) is StorageFile File)
                    {
                        if (!await File.CheckExist().ConfigureAwait(true))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                            };
                            _ = await Dialog.ShowAsync().ConfigureAwait(true);

                            await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                            return;
                        }

                        string AdminExcuteProgram = null;
                        if (ApplicationData.Current.LocalSettings.Values["AdminProgramForExcute"] is string ProgramExcute)
                        {
                            string SaveUnit = ProgramExcute.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault((Item) => Item.Split('|')[0] == TabTarget.Type);
                            if (!string.IsNullOrEmpty(SaveUnit))
                            {
                                AdminExcuteProgram = SaveUnit.Split('|')[1];
                            }
                        }

                        if (!string.IsNullOrEmpty(AdminExcuteProgram) && AdminExcuteProgram != Globalization.GetString("RX_BuildIn_Viewer_Name"))
                        {
                            bool IsExcuted = false;
                            foreach (string Path in await SQLite.Current.GetProgramPickerRecordAsync(TabTarget.Type).ConfigureAwait(true))
                            {
                                try
                                {
                                    StorageFile ExcuteFile = await StorageFile.GetFileFromPathAsync(Path);

                                    string AppName = Convert.ToString((await ExcuteFile.Properties.RetrievePropertiesAsync(new string[] { "System.FileDescription" }))["System.FileDescription"]);

                                    if (AppName == AdminExcuteProgram || ExcuteFile.DisplayName == AdminExcuteProgram)
                                    {
                                        await FullTrustExcutorController.Current.RunAsync(Path, TabTarget.Path).ConfigureAwait(true);
                                        IsExcuted = true;
                                        break;
                                    }
                                }
                                catch (Exception)
                                {
                                    await SQLite.Current.DeleteProgramPickerRecordAsync(TabTarget.Type, Path).ConfigureAwait(true);
                                }
                            }

                            if (!IsExcuted)
                            {
                                if ((await Launcher.FindFileHandlersAsync(TabTarget.Type)).FirstOrDefault((Item) => Item.DisplayInfo.DisplayName == AdminExcuteProgram) is AppInfo Info)
                                {
                                    if (!await Launcher.LaunchFileAsync(File, new LauncherOptions { TargetApplicationPackageFamilyName = Info.PackageFamilyName, DisplayApplicationPicker = false }))
                                    {
                                        ProgramPickerDialog Dialog = new ProgramPickerDialog(File);
                                        if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            if (Dialog.OpenFailed)
                                            {
                                                QueueContentDialog dialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                                                    Content = Globalization.GetString("QueueDialog_OpenFailure_Content"),
                                                    PrimaryButtonText = Globalization.GetString("QueueDialog_OpenFailure_PrimaryButton"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                                };

                                                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                                {
                                                    if (!await Launcher.LaunchFileAsync(File))
                                                    {
                                                        LauncherOptions options = new LauncherOptions
                                                        {
                                                            DisplayApplicationPicker = true
                                                        };
                                                        _ = await Launcher.LaunchFileAsync(File, options);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    ProgramPickerDialog Dialog = new ProgramPickerDialog(File);
                                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                    {
                                        if (Dialog.OpenFailed)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_TipTitle"),
                                                Content = Globalization.GetString("QueueDialog_OpenFailure_Content"),
                                                PrimaryButtonText = Globalization.GetString("QueueDialog_OpenFailure_PrimaryButton"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                            };

                                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                            {
                                                if (!await Launcher.LaunchFileAsync(File))
                                                {
                                                    LauncherOptions options = new LauncherOptions
                                                    {
                                                        DisplayApplicationPicker = true
                                                    };
                                                    _ = await Launcher.LaunchFileAsync(File, options);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            switch (File.FileType)
                            {
                                case ".jpg":
                                case ".png":
                                case ".bmp":
                                    {
                                        if (AnimationController.Current.IsEnableAnimation)
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(PhotoViewer), new Tuple<FileControl, string>(FileControlInstance, File.Name), new DrillInNavigationTransitionInfo());
                                        }
                                        else
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(PhotoViewer), new Tuple<FileControl, string>(FileControlInstance, File.Name), new SuppressNavigationTransitionInfo());
                                        }
                                        break;
                                    }
                                case ".mkv":
                                case ".mp4":
                                case ".mp3":
                                case ".flac":
                                case ".wma":
                                case ".wmv":
                                case ".m4a":
                                case ".mov":
                                case ".alac":
                                    {
                                        if (AnimationController.Current.IsEnableAnimation)
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(MediaPlayer), File, new DrillInNavigationTransitionInfo());
                                        }
                                        else
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(MediaPlayer), File, new SuppressNavigationTransitionInfo());
                                        }
                                        break;
                                    }
                                case ".txt":
                                    {
                                        if (AnimationController.Current.IsEnableAnimation)
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(TextViewer), new Tuple<FileControl, FileSystemStorageItem>(FileControlInstance, TabTarget), new DrillInNavigationTransitionInfo());
                                        }
                                        else
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(TextViewer), new Tuple<FileControl, FileSystemStorageItem>(FileControlInstance, TabTarget), new SuppressNavigationTransitionInfo());
                                        }
                                        break;
                                    }
                                case ".pdf":
                                    {
                                        if (AnimationController.Current.IsEnableAnimation)
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(PdfReader), new Tuple<Frame, StorageFile>(FileControlInstance.Nav, File), new DrillInNavigationTransitionInfo());
                                        }
                                        else
                                        {
                                            FileControlInstance.Nav.Navigate(typeof(PdfReader), new Tuple<Frame, StorageFile>(FileControlInstance.Nav, File), new SuppressNavigationTransitionInfo());
                                        }
                                        break;
                                    }
                                case ".exe":
                                case ".bat":
                                    {
                                        if (RunAsAdministrator)
                                        {
                                            await FullTrustExcutorController.Current.RunAsAdministratorAsync(TabTarget.Path).ConfigureAwait(false);
                                        }
                                        else
                                        {
                                            await FullTrustExcutorController.Current.RunAsync(TabTarget.Path).ConfigureAwait(false);
                                        }
                                        break;
                                    }
                                default:
                                    {
                                        ProgramPickerDialog Dialog = new ProgramPickerDialog(File);
                                        if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            if (Dialog.OpenFailed)
                                            {
                                                QueueContentDialog dialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                                                    Content = Globalization.GetString("QueueDialog_OpenFailure_Content"),
                                                    PrimaryButtonText = Globalization.GetString("QueueDialog_OpenFailure_PrimaryButton"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                                                };

                                                if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                                {
                                                    if (!await Launcher.LaunchFileAsync(File))
                                                    {
                                                        LauncherOptions options = new LauncherOptions
                                                        {
                                                            DisplayApplicationPicker = true
                                                        };
                                                        _ = await Launcher.LaunchFileAsync(File, options);
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                    }
                            }
                        }
                    }
                    else if ((await TabTarget.GetStorageItem().ConfigureAwait(true)) is StorageFolder Folder)
                    {
                        if (!await Folder.CheckExist().ConfigureAwait(true))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                            };
                            _ = await Dialog.ShowAsync().ConfigureAwait(true);

                            await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                            return;
                        }

                        if (SettingControl.IsDetachTreeViewAndPresenter)
                        {
                            await FileControlInstance.DisplayItemsInFolder(Folder).ConfigureAwait(true);
                        }
                        else
                        {
                            if (FileControlInstance.CurrentNode == null)
                            {
                                FileControlInstance.CurrentNode = FileControlInstance.FolderTree.RootNodes[0];
                            }

                            if (!FileControlInstance.CurrentNode.IsExpanded)
                            {
                                FileControlInstance.CurrentNode.IsExpanded = true;
                            }

                            TreeViewNode TargetNode = await FileControlInstance.FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(TabTarget.Path, (FileControlInstance.FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                            if (TargetNode != null)
                            {
                                await FileControlInstance.DisplayItemsInFolder(TargetNode).ConfigureAwait(true);
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    ExceptionTracer.RequestBlueScreen(ex);
                }
                finally
                {
                    Interlocked.Exchange(ref TabTarget, null);
                }
            }
        }

        public async void VideoEdit_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (GeneralTransformer.IsAnyTransformTaskRunning)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                    Content = Globalization.GetString("QueueDialog_TaskWorking_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile File)
            {
                VideoEditDialog Dialog = new VideoEditDialog(File);
                if ((await Dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                {
                    StorageFile ExportFile = await FileControlInstance.CurrentFolder.CreateFileAsync($"{File.DisplayName} - {Globalization.GetString("Crop_Image_Name_Tail")}{Dialog.ExportFileType}", CreationCollisionOption.GenerateUniqueName);

                    await GeneralTransformer.GenerateCroppedVideoFromOriginAsync(ExportFile, Dialog.Composition, Dialog.MediaEncoding, Dialog.TrimmingPreference).ConfigureAwait(true);
                }
            }
        }

        public async void VideoMerge_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (GeneralTransformer.IsAnyTransformTaskRunning)
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_TipTitle"),
                    Content = Globalization.GetString("QueueDialog_TaskWorking_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile Item)
            {
                VideoMergeDialog Dialog = new VideoMergeDialog(Item);
                if ((await Dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                {
                    StorageFile ExportFile = await FileControlInstance.CurrentFolder.CreateFileAsync($"{Item.DisplayName} - {Globalization.GetString("Merge_Image_Name_Tail")}{Dialog.ExportFileType}", CreationCollisionOption.GenerateUniqueName);

                    await GeneralTransformer.GenerateMergeVideoFromOriginAsync(ExportFile, Dialog.Composition, Dialog.MediaEncoding).ConfigureAwait(true);
                }
            }
        }

        public async void ChooseOtherApp_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile Item)
            {
                ProgramPickerDialog Dialog = new ProgramPickerDialog(Item);
                if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                {
                    if (Dialog.OpenFailed)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_TipTitle"),
                            Content = Globalization.GetString("QueueDialog_OpenFailure_Content"),
                            PrimaryButtonText = Globalization.GetString("QueueDialog_OpenFailure_PrimaryButton"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                        };

                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                        {
                            if (!await Launcher.LaunchFileAsync(Item))
                            {
                                LauncherOptions options = new LauncherOptions
                                {
                                    DisplayApplicationPicker = true
                                };
                                _ = await Launcher.LaunchFileAsync(Item, options);
                            }
                        }
                    }
                    else if (Dialog.ContinueUseInnerViewer)
                    {
                        await EnterSelectedItem(SelectedItem).ConfigureAwait(false);
                    }
                }
            }
        }

        public async void RunWithSystemAuthority_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItem != null)
            {
                await EnterSelectedItem(SelectedItem, true).ConfigureAwait(false);
            }
        }

        private void ListHeaderName_Click(object sender, RoutedEventArgs e)
        {
            if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Name, SortDirection.Descending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
            else
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Name, SortDirection.Ascending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
        }

        private void ListHeaderModifiedTime_Click(object sender, RoutedEventArgs e)
        {
            if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.ModifiedTime, SortDirection.Descending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
            else
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.ModifiedTime, SortDirection.Ascending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
        }

        private void ListHeaderType_Click(object sender, RoutedEventArgs e)
        {
            if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Type, SortDirection.Descending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
            else
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Type, SortDirection.Ascending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
        }

        private void ListHeaderSize_Click(object sender, RoutedEventArgs e)
        {
            if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Size, SortDirection.Descending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);
                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }

            }
            else
            {
                SortCollectionGenerator.Current.ModifySortWay(SortTarget.Size, SortDirection.Ascending);

                List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);
                FileCollection.Clear();

                foreach (FileSystemStorageItem Item in SortResult)
                {
                    FileCollection.Add(Item);
                }
            }
        }

        private void QRTeachTip_Closing(Microsoft.UI.Xaml.Controls.TeachingTip sender, Microsoft.UI.Xaml.Controls.TeachingTipClosingEventArgs args)
        {
            QRImage.Source = null;
            WiFiProvider.Dispose();
            WiFiProvider = null;
        }

        public async void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            NewFileDialog Dialog = new NewFileDialog();
            if ((await Dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
            {
                try
                {
                    StorageFile NewFile = null;

                    switch (Path.GetExtension(Dialog.NewFileName))
                    {
                        case ".zip":
                            {
                                NewFile = await SpecialTypeGenerator.Current.CreateZipAsync(FileControlInstance.CurrentFolder, Dialog.NewFileName).ConfigureAwait(true);
                                break;
                            }
                        case ".rtf":
                            {
                                NewFile = await SpecialTypeGenerator.Current.CreateRtfAsync(FileControlInstance.CurrentFolder, Dialog.NewFileName).ConfigureAwait(true);
                                break;
                            }
                        case ".xlsx":
                            {
                                NewFile = await SpecialTypeGenerator.Current.CreateExcelAsync(FileControlInstance.CurrentFolder, Dialog.NewFileName).ConfigureAwait(true);
                                break;
                            }
                        default:
                            {
                                NewFile = await FileControlInstance.CurrentFolder.CreateFileAsync(Dialog.NewFileName, CreationCollisionOption.GenerateUniqueName);
                                break;
                            }
                    }

                    if (NewFile == null)
                    {
                        throw new UnauthorizedAccessException();
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedCreateNewFile_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                    }
                }
            }
        }

        public async void CompressFolder_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            StorageFolder Item = (await SelectedItem.GetStorageItem().ConfigureAwait(true)) as StorageFolder;

            if (!await Item.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                };
                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                return;
            }

            ZipDialog dialog = new ZipDialog(true, Item.DisplayName);

            if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
            {
                await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Compressing")).ConfigureAwait(true);

                if (dialog.IsCryptionEnable)
                {
                    await CreateZipAsync(Item, dialog.FileName, (int)dialog.Level, true, dialog.Key, dialog.Password).ConfigureAwait(true);
                }
                else
                {
                    await CreateZipAsync(Item, dialog.FileName, (int)dialog.Level).ConfigureAwait(true);
                }
            }

            await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
        }

        private void ViewControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                if (e.Modifiers.HasFlag(DragDropModifiers.Control))
                {
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} {FileControlInstance.CurrentFolder.DisplayName}";
                }
                else
                {
                    e.AcceptedOperation = DataPackageOperation.Move;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} {FileControlInstance.CurrentFolder.DisplayName}";
                }

                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private async void Item_Drop(object sender, DragEventArgs e)
        {
            var Deferral = e.GetDeferral();

            if (Interlocked.Exchange(ref DropLock, 1) == 0)
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    List<IStorageItem> DragItemList = (await e.DataView.GetStorageItemsAsync()).ToList();

                    try
                    {
                        if ((sender as SelectorItem).Content is FileSystemStorageItem Item)
                        {
                            StorageFolder TargetFolder = (await Item.GetStorageItem().ConfigureAwait(true)) as StorageFolder;

                            if (DragItemList.Contains(TargetFolder))
                            {
                                QueueContentDialog Dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_DragIncludeFolderError"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };
                                _ = await Dialog.ShowAsync().ConfigureAwait(true);

                                return;
                            }

                            switch (e.AcceptedOperation)
                            {
                                case DataPackageOperation.Copy:
                                    {
                                        bool IsItemNotFound = false;
                                        bool IsUnauthorized = false;
                                        bool IsOperateFailed = false;

                                        await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                                        try
                                        {
                                            if (FileControlInstance.IsNetworkDevice)
                                            {
                                                foreach (IStorageItem DragItem in DragItemList)
                                                {
                                                    if (DragItem is StorageFile File)
                                                    {
                                                        await File.CopyAsync(TargetFolder, File.Name, NameCollisionOption.GenerateUniqueName);
                                                    }
                                                    else if (DragItem is StorageFolder Folder)
                                                    {
                                                        StorageFolder NewFolder = await TargetFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);
                                                        await Folder.CopySubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                                        if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                        {
                                                            if (FileControlInstance.CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == TargetFolder.Path) is TreeViewNode Node)
                                                            {
                                                                Node.HasUnrealizedChildren = true;

                                                                if (Node.IsExpanded)
                                                                {
                                                                    Node.Children.Add(new TreeViewNode
                                                                    {
                                                                        Content = new TreeViewNodeContent(NewFolder),
                                                                        HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                                    });
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                await FullTrustExcutorController.Current.CopyAsync(DragItemList, FileControlInstance.CurrentFolder, (s, arg) =>
                                                {
                                                    FileControlInstance.ProBar.IsIndeterminate = false;
                                                    FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                }).ConfigureAwait(true);

                                                if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                {
                                                    await FileControlInstance.FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
                                                }
                                            }
                                        }
                                        catch (FileNotFoundException)
                                        {
                                            IsItemNotFound = true;
                                        }
                                        catch (InvalidOperationException)
                                        {
                                            IsOperateFailed = true;
                                        }
                                        catch (Exception)
                                        {
                                            IsUnauthorized = true;
                                        }

                                        if (IsItemNotFound)
                                        {
                                            QueueContentDialog Dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_CopyFailForNotExist_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };
                                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        else if (IsUnauthorized)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                                            };

                                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                            {
                                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                                            }
                                        }
                                        else if (IsOperateFailed)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_CopyFailUnexpectError_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };
                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }

                                        break;
                                    }
                                case DataPackageOperation.Move:
                                    {
                                        await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                                        bool IsItemNotFound = false;
                                        bool IsUnauthorized = false;
                                        bool IsCaptured = false;
                                        bool IsOperateFailed = false;

                                        try
                                        {
                                            if (FileControlInstance.IsNetworkDevice)
                                            {
                                                foreach (IStorageItem DragItem in DragItemList)
                                                {
                                                    if (DragItem is StorageFile File)
                                                    {
                                                        await File.MoveAsync(TargetFolder, File.Name, NameCollisionOption.GenerateUniqueName);
                                                    }
                                                    else if (DragItem is StorageFolder Folder)
                                                    {
                                                        StorageFolder NewFolder = await TargetFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);

                                                        await Folder.MoveSubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                                        if (FileCollection.FirstOrDefault((Item) => Item.Path == Folder.Path) is FileSystemStorageItem RemoveItem)
                                                        {
                                                            FileCollection.Remove(RemoveItem);
                                                        }

                                                        if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                        {
                                                            if (FileControlInstance.CurrentNode.IsExpanded)
                                                            {
                                                                if (FileControlInstance.CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == Folder.Path) is TreeViewNode RemoveNode)
                                                                {
                                                                    FileControlInstance.CurrentNode.Children.Remove(RemoveNode);
                                                                }
                                                            }

                                                            FileControlInstance.CurrentNode.HasUnrealizedChildren = (await FileControlInstance.CurrentFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0;

                                                            if (FileControlInstance.CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == TargetFolder.Path) is TreeViewNode Node)
                                                            {
                                                                Node.HasUnrealizedChildren = true;

                                                                if (Node.IsExpanded)
                                                                {
                                                                    Node.Children.Add(new TreeViewNode
                                                                    {
                                                                        Content = new TreeViewNodeContent(NewFolder),
                                                                        HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                                    });
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                await FullTrustExcutorController.Current.MoveAsync(DragItemList, TargetFolder, (s, arg) =>
                                                {
                                                    FileControlInstance.ProBar.IsIndeterminate = false;
                                                    FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                                }).ConfigureAwait(true);
                                            }
                                        }
                                        catch (FileNotFoundException)
                                        {
                                            IsItemNotFound = true;
                                        }
                                        catch (FileCaputureException)
                                        {
                                            IsCaptured = true;
                                        }
                                        catch (InvalidOperationException)
                                        {
                                            IsOperateFailed = true;
                                        }
                                        catch (Exception)
                                        {
                                            IsUnauthorized = true;
                                        }

                                        if (IsItemNotFound)
                                        {
                                            QueueContentDialog Dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_MoveFailForNotExist_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        else if (IsUnauthorized)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                                PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                                            };

                                            if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                            {
                                                _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                                            }
                                        }
                                        else if (IsCaptured)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };

                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }
                                        else if (IsOperateFailed)
                                        {
                                            QueueContentDialog dialog = new QueueContentDialog
                                            {
                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                Content = Globalization.GetString("QueueDialog_MoveFailUnexpectError_Content"),
                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                            };
                                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                                        }

                                        break;
                                    }
                            }
                        }
                    }
                    finally
                    {
                        DragItemList.Clear();
                        await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
                        e.Handled = true;
                        Deferral.Complete();
                        _ = Interlocked.Exchange(ref DropLock, 0);
                    }
                }
            }
        }


        private async void ViewControl_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.Count != 0)
            {
                List<IStorageItem> TempList = new List<IStorageItem>(e.Items.Count);

                foreach (object obj in e.Items)
                {
                    if (obj is FileSystemStorageItem StorageItem)
                    {
                        if (ItemPresenter.ContainerFromItem(StorageItem) is SelectorItem SItem && SItem.ContentTemplateRoot.FindChildOfType<TextBox>() is TextBox NameEditBox)
                        {
                            NameEditBox.Visibility = Visibility.Collapsed;
                        }

                        if (await StorageItem.GetStorageItem().ConfigureAwait(true) is IStorageItem Item)
                        {
                            TempList.Add(Item);
                        }
                    }
                }

                if (TempList.Count > 0)
                {
                    e.Data.SetStorageItems(TempList, false);
                }
            }
        }

        private async void ViewControl_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                args.ItemContainer.AllowDrop = false;
                args.ItemContainer.Drop -= Item_Drop;
                args.ItemContainer.DragEnter -= ItemContainer_DragEnter;
                args.ItemContainer.PointerEntered -= ItemContainer_PointerEntered;

                if (args.ItemContainer.ContentTemplateRoot.FindChildOfType<TextBox>() is TextBox NameEditBox)
                {
                    NameEditBox.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                if (args.Item is FileSystemStorageItem Item)
                {
                    if (Item.StorageType == StorageItemTypes.File)
                    {
                        await Item.LoadMoreProperty().ConfigureAwait(true);
                    }

                    if (Item.StorageType == StorageItemTypes.Folder)
                    {
                        args.ItemContainer.AllowDrop = true;
                        args.ItemContainer.Drop += Item_Drop;
                        args.ItemContainer.DragEnter += ItemContainer_DragEnter;
                    }

                    if (Item.IsHidenItem)
                    {
                        args.ItemContainer.AllowDrop = false;
                        args.ItemContainer.CanDrag = false;
                    }

                    args.ItemContainer.PointerEntered += ItemContainer_PointerEntered;
                }
            }
        }

        private void ItemContainer_DragEnter(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                if (sender is SelectorItem)
                {
                    FileSystemStorageItem Item = (sender as SelectorItem).Content as FileSystemStorageItem;

                    if (e.Modifiers.HasFlag(DragDropModifiers.Control))
                    {
                        e.AcceptedOperation = DataPackageOperation.Copy;
                        e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} {Item.Name}";
                    }
                    else
                    {
                        e.AcceptedOperation = DataPackageOperation.Move;
                        e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} {Item.Name}";
                    }

                    e.DragUIOverride.IsContentVisible = true;
                    e.DragUIOverride.IsCaptionVisible = true;
                }
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private void ItemContainer_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!SettingControl.IsDoubleClickEnable && e.KeyModifiers != VirtualKeyModifiers.Control && e.KeyModifiers != VirtualKeyModifiers.Shift)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Item)
                {
                    SelectedItem = Item;
                }
            }
        }

        private async void ViewControl_Drop(object sender, DragEventArgs e)
        {
            var Deferral = e.GetDeferral();

            if (Interlocked.Exchange(ref ViewDropLock, 1) == 0)
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    List<IStorageItem> DragItemList = (await e.DataView.GetStorageItemsAsync()).ToList();

                    try
                    {
                        StorageFolder TargetFolder = FileControlInstance.CurrentFolder;

                        if (DragItemList.Contains(TargetFolder))
                        {
                            QueueContentDialog Dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = Globalization.GetString("QueueDialog_DragIncludeFolderError"),
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                            };
                            _ = await Dialog.ShowAsync().ConfigureAwait(true);

                            return;
                        }

                        switch (e.AcceptedOperation)
                        {
                            case DataPackageOperation.Copy:
                                {
                                    bool IsItemNotFound = false;
                                    bool IsUnauthorized = false;
                                    bool IsOperateFailed = false;

                                    await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                                    try
                                    {
                                        if (FileControlInstance.IsNetworkDevice)
                                        {
                                            foreach (IStorageItem DragItem in DragItemList)
                                            {
                                                if (DragItem is StorageFile File)
                                                {
                                                    StorageFile NewFile = await File.CopyAsync(TargetFolder, File.Name, NameCollisionOption.GenerateUniqueName);

                                                    FileCollection.Add(new FileSystemStorageItem(NewFile, await NewFile.GetSizeRawDataAsync().ConfigureAwait(true), await NewFile.GetThumbnailBitmapAsync().ConfigureAwait(true), await NewFile.GetModifiedTimeAsync().ConfigureAwait(true)));
                                                }
                                                else if (DragItem is StorageFolder Folder)
                                                {
                                                    StorageFolder NewFolder = await TargetFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);
                                                    await Folder.CopySubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                                    FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));

                                                    if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                    {
                                                        FileControlInstance.CurrentNode.HasUnrealizedChildren = true;

                                                        if (FileControlInstance.CurrentNode.IsExpanded)
                                                        {
                                                            FileControlInstance.CurrentNode.Children.Add(new TreeViewNode
                                                            {
                                                                Content = new TreeViewNodeContent(NewFolder),
                                                                HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            await FullTrustExcutorController.Current.CopyAsync(DragItemList, TargetFolder, (s, arg) =>
                                            {
                                                FileControlInstance.ProBar.IsIndeterminate = false;
                                                FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                            }).ConfigureAwait(true);
                                        }
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        IsItemNotFound = true;
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        IsOperateFailed = true;
                                    }
                                    catch (Exception)
                                    {
                                        IsUnauthorized = true;
                                    }

                                    if (IsItemNotFound)
                                    {
                                        QueueContentDialog Dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_CopyFailForNotExist_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };
                                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    else if (IsUnauthorized)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                            PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                                        };

                                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                                        }
                                    }
                                    else if (IsOperateFailed)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_CopyFailUnexpectError_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };
                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }

                                    break;
                                }
                            case DataPackageOperation.Move:
                                {
                                    if (DragItemList.Select((Item) => Item.Path).All((Item) => Path.GetDirectoryName(Item) == FileControlInstance.CurrentFolder.Path))
                                    {
                                        return;
                                    }

                                    await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                                    bool IsItemNotFound = false;
                                    bool IsUnauthorized = false;
                                    bool IsCaptured = false;
                                    bool IsOperateFailed = false;

                                    try
                                    {
                                        if (FileControlInstance.IsNetworkDevice)
                                        {
                                            foreach (IStorageItem DragItem in DragItemList)
                                            {
                                                if (DragItem is StorageFile File)
                                                {
                                                    await File.MoveAsync(TargetFolder, File.Name, NameCollisionOption.GenerateUniqueName);

                                                    FileCollection.Add(new FileSystemStorageItem(File, await File.GetSizeRawDataAsync().ConfigureAwait(true), await File.GetThumbnailBitmapAsync().ConfigureAwait(true), await File.GetModifiedTimeAsync().ConfigureAwait(true)));
                                                }
                                                else if (DragItem is StorageFolder Folder)
                                                {
                                                    StorageFolder NewFolder = await TargetFolder.CreateFolderAsync(Folder.Name, CreationCollisionOption.GenerateUniqueName);

                                                    await Folder.MoveSubFilesAndSubFoldersAsync(NewFolder).ConfigureAwait(true);

                                                    FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));

                                                    if (!SettingControl.IsDetachTreeViewAndPresenter)
                                                    {
                                                        if (TabViewContainer.ThisPage.TabViewControl.TabItems.Select((Tab) => ((Tab as Microsoft.UI.Xaml.Controls.TabViewItem)?.Content as Frame)?.Content as FileControl).Where((Control) => Control != null).FirstOrDefault((Control) => Control.CurrentFolder.Path == Path.GetDirectoryName(Folder.Path)) is FileControl Control && Control.IsNetworkDevice)
                                                        {
                                                            if (Control.CurrentNode.IsExpanded)
                                                            {
                                                                if (Control.CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == Folder.Path) is TreeViewNode Node)
                                                                {
                                                                    Control.CurrentNode.Children.Remove(Node);
                                                                }
                                                            }

                                                            Control.CurrentNode.HasUnrealizedChildren = (await Control.CurrentFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0;

                                                            TabViewContainer.ThisPage.FFInstanceContainer[Control].Refresh_Click(null, null);
                                                        }

                                                        FileControlInstance.CurrentNode.HasUnrealizedChildren = true;

                                                        if (FileControlInstance.CurrentNode.IsExpanded)
                                                        {
                                                            FileControlInstance.CurrentNode.Children.Add(new TreeViewNode
                                                            {
                                                                Content = new TreeViewNodeContent(NewFolder),
                                                                HasUnrealizedChildren = (await NewFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            await FullTrustExcutorController.Current.MoveAsync(DragItemList, TargetFolder, (s, arg) =>
                                            {
                                                FileControlInstance.ProBar.IsIndeterminate = false;
                                                FileControlInstance.ProBar.Value = arg.ProgressPercentage;
                                            }).ConfigureAwait(true);
                                        }
                                    }
                                    catch (FileCaputureException)
                                    {
                                        IsCaptured = true;
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        IsItemNotFound = true;
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        IsOperateFailed = true;
                                    }
                                    catch (Exception)
                                    {
                                        IsUnauthorized = true;
                                    }

                                    if (IsItemNotFound)
                                    {
                                        QueueContentDialog Dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_MoveFailForNotExist_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    else if (IsUnauthorized)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_UnauthorizedPaste_Content"),
                                            PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                                        };

                                        if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                                        {
                                            _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                                        }
                                    }
                                    else if (IsCaptured)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };

                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }
                                    else if (IsOperateFailed)
                                    {
                                        QueueContentDialog dialog = new QueueContentDialog
                                        {
                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                            Content = Globalization.GetString("QueueDialog_MoveFailUnexpectError_Content"),
                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                        };
                                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                                    }

                                    break;
                                }
                        }
                    }
                    finally
                    {
                        DragItemList.Clear();
                        await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
                        e.Handled = true;
                        Deferral.Complete();
                        _ = Interlocked.Exchange(ref ViewDropLock, 0);
                    }
                }
            }

        }

        private void ViewControl_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                if (ItemPresenter is GridView)
                {
                    if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Context)
                    {
                        if (SelectedItems.Count <= 1 || !SelectedItems.Contains(Context))
                        {
                            if (Context.IsHidenItem)
                            {
                                ItemPresenter.ContextFlyout = HiddenItemFlyout;
                            }
                            else
                            {
                                ItemPresenter.ContextFlyout = Context.StorageType == StorageItemTypes.Folder ? FolderFlyout : FileFlyout;
                            }

                            SelectedItem = Context;
                        }
                        else
                        {
                            if (SelectedItems.All((Item) => !Item.IsHidenItem))
                            {
                                ItemPresenter.ContextFlyout = MixedFlyout;
                            }
                            else
                            {
                                ItemPresenter.ContextFlyout = null;
                            }
                        }
                    }
                    else
                    {
                        SelectedItem = null;
                        ItemPresenter.ContextFlyout = EmptyFlyout;
                    }
                }
                else
                {
                    if (e.OriginalSource is ListViewItemPresenter || (e.OriginalSource as FrameworkElement)?.Name == "EmptyTextblock")
                    {
                        SelectedItem = null;
                        ItemPresenter.ContextFlyout = EmptyFlyout;
                    }
                    else
                    {
                        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Context)
                        {
                            if (SelectedItems.Count <= 1 || !SelectedItems.Contains(Context))
                            {
                                if (Context.IsHidenItem)
                                {
                                    ItemPresenter.ContextFlyout = HiddenItemFlyout;
                                }
                                else
                                {
                                    ItemPresenter.ContextFlyout = Context.StorageType == StorageItemTypes.Folder ? FolderFlyout : FileFlyout;
                                }

                                SelectedItem = Context;
                            }
                            else
                            {
                                if (SelectedItems.All((Item) => !Item.IsHidenItem))
                                {
                                    ItemPresenter.ContextFlyout = MixedFlyout;
                                }
                                else
                                {
                                    ItemPresenter.ContextFlyout = null;
                                }
                            }
                        }
                        else
                        {
                            SelectedItem = null;
                            ItemPresenter.ContextFlyout = EmptyFlyout;
                        }
                    }
                }
            }
        }

        public async void MixZip_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            foreach (FileSystemStorageItem Item in SelectedItems)
            {
                if (Item.StorageType == StorageItemTypes.Folder)
                {
                    StorageFolder Folder = (await Item.GetStorageItem().ConfigureAwait(true)) as StorageFolder;

                    if (!await Folder.CheckExist().ConfigureAwait(true))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);

                        await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                        return;
                    }
                }
                else
                {
                    StorageFile File = (await Item.GetStorageItem().ConfigureAwait(true)) as StorageFile;

                    if (!await File.CheckExist().ConfigureAwait(true))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);

                        await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(false);
                        return;
                    }
                }
            }

            bool IsCompress = false;
            if (SelectedItems.All((Item) => Item.StorageType == StorageItemTypes.File))
            {
                if (SelectedItems.All((Item) => Item.Type == ".zip"))
                {
                    IsCompress = false;
                }
                else if (SelectedItems.All((Item) => Item.Type != ".zip"))
                {
                    IsCompress = true;
                }
                else
                {
                    return;
                }
            }
            else if (SelectedItems.All((Item) => Item.StorageType == StorageItemTypes.Folder))
            {
                IsCompress = true;
            }
            else
            {
                if (SelectedItems.Where((It) => It.StorageType == StorageItemTypes.File).All((Item) => Item.Type != ".zip"))
                {
                    IsCompress = true;
                }
                else
                {
                    return;
                }
            }

            if (IsCompress)
            {
                ZipDialog dialog = new ZipDialog(true, Globalization.GetString("Zip_Admin_Name_Text"));

                if ((await dialog.ShowAsync().ConfigureAwait(true)) == ContentDialogResult.Primary)
                {
                    await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Compressing")).ConfigureAwait(true);

                    if (dialog.IsCryptionEnable)
                    {
                        await CreateZipAsync(SelectedItems, dialog.FileName, (int)dialog.Level, true, dialog.Key, dialog.Password).ConfigureAwait(true);
                    }
                    else
                    {
                        await CreateZipAsync(SelectedItems, dialog.FileName, (int)dialog.Level).ConfigureAwait(true);
                    }

                    await FileControlInstance.LoadingActivation(false).ConfigureAwait(true);
                }
            }
            else
            {
                foreach (FileSystemStorageItem Item in SelectedItems)
                {
                    StorageFile File = (await Item.GetStorageItem().ConfigureAwait(true)) as StorageFile;

                    await UnZipAsync(File).ConfigureAwait(true);
                }
            }

        }

        public async void TryUnlock_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (SelectedItem is FileSystemStorageItem Item && Item.StorageType == StorageItemTypes.File)
            {
                try
                {
                    await FileControlInstance.LoadingActivation(true, Globalization.GetString("Progress_Tip_Unlock")).ConfigureAwait(true);

                    if (await FullTrustExcutorController.Current.TryUnlockFileOccupy(Item.Path).ConfigureAwait(true))
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_TipTitle"),
                            Content = Globalization.GetString("QueueDialog_Unlock_Success_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        QueueContentDialog Dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_TipTitle"),
                            Content = Globalization.GetString("QueueDialog_Unlock_Failure_Content"),
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                        };
                        _ = await Dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
                catch (FileNotFoundException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_TipTitle"),
                        Content = Globalization.GetString("QueueDialog_Unlock_FileNotFound_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                }
                catch (UnlockException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_TipTitle"),
                        Content = Globalization.GetString("QueueDialog_Unlock_NoLock_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
                catch
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_TipTitle"),
                        Content = Globalization.GetString("QueueDialog_Unlock_UnexpectedError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
                finally
                {
                    await FileControlInstance.LoadingActivation(false).ConfigureAwait(false);
                }
            }
        }

        public async void CalculateHash_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            try
            {
                if (HashTeachTip.IsOpen)
                {
                    HashTeachTip.IsOpen = false;
                }

                await Task.Run(() =>
                {
                    SpinWait.SpinUntil(() => HashCancellation == null);
                }).ConfigureAwait(true);

                if ((await SelectedItem.GetStorageItem().ConfigureAwait(true)) is StorageFile Item)
                {
                    Hash_Crc32.IsEnabled = false;
                    Hash_SHA1.IsEnabled = false;
                    Hash_SHA256.IsEnabled = false;
                    Hash_MD5.IsEnabled = false;

                    Hash_Crc32.Text = string.Empty;
                    Hash_SHA1.Text = string.Empty;
                    Hash_SHA256.Text = string.Empty;
                    Hash_MD5.Text = string.Empty;

                    await Task.Delay(500).ConfigureAwait(true);
                    HashTeachTip.Target = ItemPresenter.ContainerFromItem(SelectedItem) as FrameworkElement;
                    HashTeachTip.IsOpen = true;

                    using (HashCancellation = new CancellationTokenSource())
                    {
                        var task1 = Item.ComputeSHA256Hash(HashCancellation.Token);
                        Hash_SHA256.IsEnabled = true;

                        var task2 = Item.ComputeCrc32Hash(HashCancellation.Token);
                        Hash_Crc32.IsEnabled = true;

                        var task4 = Item.ComputeMD5Hash(HashCancellation.Token);
                        Hash_MD5.IsEnabled = true;

                        var task3 = Item.ComputeSHA1Hash(HashCancellation.Token);
                        Hash_SHA1.IsEnabled = true;

                        Hash_MD5.Text = await task4.ConfigureAwait(true);
                        Hash_Crc32.Text = await task2.ConfigureAwait(true);
                        Hash_SHA1.Text = await task3.ConfigureAwait(true);
                        Hash_SHA256.Text = await task1.ConfigureAwait(true);
                    }
                }
                else
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await FileControlInstance.DisplayItemsInFolder(FileControlInstance.CurrentFolder, true).ConfigureAwait(true);
                }
            }
            catch
            {
                Debug.WriteLine("Error: CalculateHash failed");
            }
            finally
            {
                HashCancellation = null;
            }
        }

        private void Hash_Crc32_Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage Package = new DataPackage();
            Package.SetText(Hash_Crc32.Text);
            Clipboard.SetContent(Package);
        }

        private void Hash_SHA1_Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage Package = new DataPackage();
            Package.SetText(Hash_SHA1.Text);
            Clipboard.SetContent(Package);
        }

        private void Hash_SHA256_Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage Package = new DataPackage();
            Package.SetText(Hash_SHA256.Text);
            Clipboard.SetContent(Package);
        }

        private void Hash_MD5_Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage Package = new DataPackage();
            Package.SetText(Hash_MD5.Text);
            Clipboard.SetContent(Package);
        }

        private void HashTeachTip_Closing(Microsoft.UI.Xaml.Controls.TeachingTip sender, Microsoft.UI.Xaml.Controls.TeachingTipClosingEventArgs args)
        {
            HashCancellation?.Cancel();
        }

        public async void OpenInTerminal_Click(object sender, RoutedEventArgs e)
        {
            switch (await Launcher.QueryUriSupportAsync(new Uri("ms-windows-store:"), LaunchQuerySupportType.Uri, "Microsoft.WindowsTerminal_8wekyb3d8bbwe"))
            {
                case LaunchQuerySupportStatus.Available:
                case LaunchQuerySupportStatus.NotSupported:
                    {
                        await FullTrustExcutorController.Current.RunAsync("wt.exe", $"/d {FileControlInstance.CurrentFolder.Path}").ConfigureAwait(false);
                        break;
                    }
                default:
                    {
                        string ExcutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
                        await FullTrustExcutorController.Current.RunAsAdministratorAsync(ExcutePath, $"-NoExit -Command \"Set-Location '{FileControlInstance.CurrentFolder.Path}'\"").ConfigureAwait(false);
                        break;
                    }
            }
        }

        public async void OpenFolderInNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is FileSystemStorageItem Item && Item.StorageType == StorageItemTypes.Folder)
            {
                await TabViewContainer.ThisPage.CreateNewTabAndOpenTargetFolder(Item.Path).ConfigureAwait(false);
            }
        }

        private void NameLabel_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            TextBlock NameLabel = (TextBlock)sender;

            if ((e.GetCurrentPoint(NameLabel).Properties.IsLeftButtonPressed || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) && SettingControl.IsDoubleClickEnable)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItem Item)
                {
                    if (Item.IsHidenItem)
                    {
                        return;
                    }

                    if (SelectedItem == Item)
                    {
                        TimeSpan ClickSpan = DateTimeOffset.Now - LastClickTime;

                        if (ClickSpan.TotalMilliseconds > 1200)
                        {
                            NameLabel.Visibility = Visibility.Collapsed;
                            CurrentNameEditItem = Item;

                            if ((NameLabel.Parent as FrameworkElement).FindName("NameEditBox") is TextBox EditBox)
                            {
                                EditBox.Text = NameLabel.Text;
                                EditBox.Visibility = Visibility.Visible;
                                EditBox.Focus(FocusState.Programmatic);
                            }

                            FileControlInstance.IsSearchOrPathBoxFocused = true;
                        }
                    }

                    LastClickTime = DateTimeOffset.Now;
                }
            }
        }

        private async void NameEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox NameEditBox = (TextBox)sender;

            if ((NameEditBox.Parent as FrameworkElement).FindName("NameLabel") is TextBlock NameLabel && CurrentNameEditItem != null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(NameEditBox.Text) || !FileSystemItemNameChecker.IsValid(NameEditBox.Text))
                    {
                        InvalidNameTip.Target = NameLabel;
                        InvalidNameTip.IsOpen = true;
                        return;
                    }

                    if (CurrentNameEditItem.Name != NameEditBox.Text && await CurrentNameEditItem.GetStorageItem().ConfigureAwait(true) is IStorageItem StorageItem)
                    {
                        await StorageItem.RenameAsync(NameEditBox.Text);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedRenameFolder_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton")
                    };

                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(FileControlInstance.CurrentFolder);
                    }
                }
                catch
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_RenameExist_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_ContinueButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CancelButton")
                    };

                    if (await Dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary && await CurrentNameEditItem.GetStorageItem().ConfigureAwait(true) is IStorageItem StorageItem)
                    {
                        await StorageItem.RenameAsync(NameEditBox.Text, NameCollisionOption.GenerateUniqueName);
                    }
                }
                finally
                {
                    NameEditBox.Visibility = Visibility.Collapsed;

                    NameLabel.Visibility = Visibility.Visible;

                    LastClickTime = DateTimeOffset.MaxValue;

                    FileControlInstance.IsSearchOrPathBoxFocused = false;
                }
            }
        }

        private void GetFocus_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ItemPresenter.Focus(FocusState.Programmatic);
        }

        public async void OpenFolderInNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is FileSystemStorageItem Item && Item.StorageType == StorageItemTypes.Folder)
            {
                await Launcher.LaunchUriAsync(new Uri($"rx-explorer:{Uri.EscapeDataString(Item.Path)}"));
            }
        }

        public async void Undo_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            await Ctrl_Z_Click().ConfigureAwait(false);
        }

        public async void RemoveHidden_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (await FullTrustExcutorController.Current.RemoveHiddenAttribute(SelectedItem.Path).ConfigureAwait(true))
            {
                SelectedItem.SetHiddenProperty(false);
            }
            else
            {
                QueueContentDialog Dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_RemoveHiddenError_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await Dialog.ShowAsync().ConfigureAwait(false);
            }
        }

        public async void OpenHiddenItemExplorer_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            if (!await Launcher.LaunchFolderPathAsync(SelectedItem.Path))
            {
                await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(SelectedItem.Path));
            }
        }

        private void NameEditBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            if (args.NewText.Any((Item) => Path.GetInvalidFileNameChars().Contains(Item)))
            {
                args.Cancel = true;

                if ((sender.Parent as FrameworkElement).FindName("NameLabel") is TextBlock NameLabel)
                {
                    InvalidCharTip.Target = NameLabel;
                    InvalidCharTip.IsOpen = true;
                }
            }
        }

        public void OrderByName_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortTarget.Name, Desc.IsChecked ? SortDirection.Descending : SortDirection.Ascending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void OrderByTime_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortTarget.ModifiedTime, Desc.IsChecked ? SortDirection.Descending : SortDirection.Ascending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void OrderByType_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortTarget.Type, Desc.IsChecked ? SortDirection.Descending : SortDirection.Ascending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void OrderBySize_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortTarget.Size, Desc.IsChecked ? SortDirection.Descending : SortDirection.Ascending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void Desc_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortDirection: SortDirection.Descending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void Asc_Click(object sender, RoutedEventArgs e)
        {
            Restore();

            SortCollectionGenerator.Current.ModifySortWay(SortDirection: SortDirection.Ascending);

            List<FileSystemStorageItem> SortResult = SortCollectionGenerator.Current.GetSortedCollection(FileCollection);

            FileCollection.Clear();

            foreach (FileSystemStorageItem Item in SortResult)
            {
                FileCollection.Add(Item);
            }
        }

        public void SortMenuFlyout_Opening(object sender, object e)
        {
            if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
            {
                Desc.IsChecked = false;
                Asc.IsChecked = true;
            }
            else
            {
                Asc.IsChecked = false;
                Desc.IsChecked = true;
            }

            switch (SortCollectionGenerator.Current.SortTarget)
            {
                case SortTarget.Name:
                    {
                        OrderByType.IsChecked = false;
                        OrderByTime.IsChecked = false;
                        OrderBySize.IsChecked = false;
                        OrderByName.IsChecked = true;
                        break;
                    }
                case SortTarget.Type:
                    {
                        OrderByTime.IsChecked = false;
                        OrderBySize.IsChecked = false;
                        OrderByName.IsChecked = false;
                        OrderByType.IsChecked = true;
                        break;
                    }
                case SortTarget.ModifiedTime:
                    {
                        OrderBySize.IsChecked = false;
                        OrderByName.IsChecked = false;
                        OrderByType.IsChecked = false;
                        OrderByTime.IsChecked = true;
                        break;
                    }
                case SortTarget.Size:
                    {
                        OrderByName.IsChecked = false;
                        OrderByType.IsChecked = false;
                        OrderByTime.IsChecked = false;
                        OrderBySize.IsChecked = true;
                        break;
                    }
            }
        }
    }
}

