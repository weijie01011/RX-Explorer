﻿using Microsoft.Toolkit.Uwp.UI.Animations;
using Microsoft.UI.Xaml.Controls;
using RX_Explorer.Class;
using RX_Explorer.Dialog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using TreeView = Microsoft.UI.Xaml.Controls.TreeView;
using TreeViewCollapsedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewCollapsedEventArgs;
using TreeViewExpandingEventArgs = Microsoft.UI.Xaml.Controls.TreeViewExpandingEventArgs;
using TreeViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs;
using TreeViewNode = Microsoft.UI.Xaml.Controls.TreeViewNode;

namespace RX_Explorer
{
    public sealed partial class FileControl : Page, IDisposable
    {
        private volatile TreeViewNode currentnode;

        public TreeViewNode CurrentNode
        {
            get
            {
                return currentnode;
            }
            set
            {
                if (!IsNetworkDevice)
                {
                    TabViewContainer.ThisPage.FFInstanceContainer[this].AreaWatcher.SetCurrentLocation((value?.Content as TreeViewNodeContent)?.Path);
                }

                if (value != null && value.Content is TreeViewNodeContent Content)
                {
                    FolderTree.SelectNode(value);

                    UpdateAddressButton(Content.Path);

                    TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter.Focus(FocusState.Programmatic);

                    string PlaceText;
                    if (Content.DisplayName.Length > 22)
                    {
                        PlaceText = Content.DisplayName.Substring(0, 22) + "...";
                    }
                    else
                    {
                        PlaceText = Content.DisplayName;
                    }

                    GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {PlaceText}";

                    GoParentFolder.IsEnabled = !FolderTree.RootNodes.Contains(value);
                    GoBackRecord.IsEnabled = RecordIndex > 0;
                    GoForwardRecord.IsEnabled = RecordIndex < GoAndBackRecord.Count - 1;

                    if (TabItem != null)
                    {
                        TabItem.Header = Content.DisplayName;
                    }
                }

                currentnode = value;
            }
        }

        private int TextChangeLockResource = 0;

        private int AddressButtonLockResource = 0;

        private int NavigateLockResource = 0;

        private int DropLockResource = 0;

        private string AddressBoxTextBackup;

        private volatile StorageFolder currentFolder;

        private readonly SemaphoreSlim EnterLock = new SemaphoreSlim(1, 1);

        public bool IsNetworkDevice { get; private set; } = false;

        private CancellationTokenSource AddItemCancellation;

        public StorageFolder CurrentFolder
        {
            get
            {
                if (SettingControl.IsDetachTreeViewAndPresenter)
                {
                    return currentFolder ??= (CurrentNode?.Content as TreeViewNodeContent)?.GetStorageFolderAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    return (CurrentNode?.Content as TreeViewNodeContent)?.GetStorageFolderAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            set
            {
                if (!IsNetworkDevice)
                {
                    TabViewContainer.ThisPage.FFInstanceContainer[this].AreaWatcher.SetCurrentLocation(value?.Path);
                }

                if (value != null)
                {
                    UpdateAddressButton(value.Path);

                    TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter.Focus(FocusState.Programmatic);

                    string PlaceText;
                    if (value.DisplayName.Length > 22)
                    {
                        PlaceText = value.DisplayName.Substring(0, 22) + "...";
                    }
                    else
                    {
                        PlaceText = value.DisplayName;
                    }

                    GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {PlaceText}";

                    GoParentFolder.IsEnabled = value.Path != Path.GetPathRoot(value.Path);
                    GoBackRecord.IsEnabled = RecordIndex > 0;
                    GoForwardRecord.IsEnabled = RecordIndex < GoAndBackRecord.Count - 1;

                    if (TabItem != null)
                    {
                        TabItem.Header = value.DisplayName;
                    }
                }

                currentFolder = value;
            }
        }

        private int RecordIndex
        {
            get
            {
                return recordIndex;
            }
            set
            {
                recordIndex = value;
            }
        }

        public bool IsSearchOrPathBoxFocused { get; set; } = false;

        private List<string> GoAndBackRecord = new List<string>();
        private ObservableCollection<AddressBlock> AddressButtonList = new ObservableCollection<AddressBlock>();
        private ObservableCollection<string> AddressExtentionList = new ObservableCollection<string>();
        private volatile int recordIndex = 0;
        private bool IsBackOrForwardAction = false;
        private Microsoft.UI.Xaml.Controls.TabViewItem TabItem;

        public FileControl()
        {
            InitializeComponent();

            try
            {
                if (AnimationController.Current.IsEnableAnimation)
                {
                    Nav.Navigate(typeof(FilePresenter), this, new DrillInNavigationTransitionInfo());
                }
                else
                {
                    Nav.Navigate(typeof(FilePresenter), this, new SuppressNavigationTransitionInfo());
                }

                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Tiles"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Details"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_List"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Large_Icon"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Medium_Icon"));
                ItemDisplayMode.Items.Add(Globalization.GetString("FileControl_ItemDisplayMode_Small_Icon"));

                if (ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] is int Index)
                {
                    ItemDisplayMode.SelectedIndex = Index;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] = 1;
                    ItemDisplayMode.SelectedIndex = 1;
                }
            }
            catch (Exception ex)
            {
                ExceptionTracer.RequestBlueScreen(ex);
            }
        }

        /// <summary>
        /// 激活或关闭正在加载提示
        /// </summary>
        /// <param name="IsLoading">激活或关闭</param>
        /// <param name="Info">提示内容</param>
        public async Task LoadingActivation(bool IsLoading, string Info = null)
        {
            if (IsLoading)
            {
                if (TabViewContainer.ThisPage.FFInstanceContainer[this].HasFile.Visibility == Visibility.Visible)
                {
                    TabViewContainer.ThisPage.FFInstanceContainer[this].HasFile.Visibility = Visibility.Collapsed;
                }

                ProBar.IsIndeterminate = true;
                ProgressInfo.Text = Info + "...";

                MainPage.ThisPage.IsAnyTaskRunning = true;
            }
            else
            {
                await Task.Delay(500).ConfigureAwait(true);
                MainPage.ThisPage.IsAnyTaskRunning = false;
            }

            LoadingControl.IsLoading = IsLoading;
        }

        public Task CancelAddItemOperation()
        {
            return Task.Run(() =>
            {
                AddItemCancellation?.Cancel();
                SpinWait.SpinUntil(() => AddItemCancellation == null);
            });
        }

        private async void UpdateAddressButton(string Path)
        {
            if (Interlocked.Exchange(ref AddressButtonLockResource, 1) == 0)
            {
                try
                {
                    if (string.IsNullOrEmpty(Path))
                    {
                        return;
                    }

                    if (CurrentFolder == null)
                    {
                        string RootPath = System.IO.Path.GetPathRoot(Path);

                        StorageFolder DriveRootFolder = await StorageFolder.GetFolderFromPathAsync(RootPath);
                        AddressButtonList.Add(new AddressBlock(DriveRootFolder.DisplayName));

                        PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                        while (Analysis.HasNextLevel)
                        {
                            AddressButtonList.Add(new AddressBlock(Analysis.NextRelativePath()));
                        }
                    }
                    else
                    {
                        string OriginalString = string.Join("\\", AddressButtonList.Skip(1));
                        string ActualString = System.IO.Path.Combine(System.IO.Path.GetPathRoot(CurrentFolder.Path), OriginalString);

                        List<string> IntersectList = new List<string>();
                        string[] FolderSplit = Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                        string[] ActualSplit = ActualString.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                        for (int i = 0; i < FolderSplit.Length && i < ActualSplit.Length; i++)
                        {
                            if (FolderSplit[i] == ActualSplit[i])
                            {
                                IntersectList.Add(FolderSplit[i]);
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (IntersectList.Count == 0)
                        {
                            AddressButtonList.Clear();

                            string RootPath = System.IO.Path.GetPathRoot(Path);

                            StorageFolder DriveRootFolder = await StorageFolder.GetFolderFromPathAsync(RootPath);
                            AddressButtonList.Add(new AddressBlock(DriveRootFolder.DisplayName));

                            PathAnalysis Analysis = new PathAnalysis(Path, RootPath);

                            while (Analysis.HasNextLevel)
                            {
                                AddressButtonList.Add(new AddressBlock(Analysis.NextRelativePath()));
                            }
                        }
                        else
                        {
                            for (int i = AddressButtonList.Count - 1; i >= IntersectList.Count; i--)
                            {
                                AddressButtonList.RemoveAt(i);
                            }

                            List<string> ExceptList = Path.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();

                            ExceptList.RemoveRange(0, IntersectList.Count);

                            foreach (string SubPath in ExceptList)
                            {
                                AddressButtonList.Add(new AddressBlock(SubPath));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("UpdateAddressButton throw an exception, message: " + ex.Message);
                }
                finally
                {
                    AddressButtonScrollViewer.UpdateLayout();

                    if (AddressButtonScrollViewer.ActualWidth < AddressButtonScrollViewer.ExtentWidth)
                    {
                        AddressButtonScrollViewer.ChangeView(AddressButtonScrollViewer.ExtentWidth, null, null);
                    }

                    _ = Interlocked.Exchange(ref AddressButtonLockResource, 0);
                }
            }
        }

        private async Task OpenTargetFolder(StorageFolder Folder)
        {
            TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.Clear();
            TabViewContainer.ThisPage.FFInstanceContainer[this].HasFile.Visibility = Visibility.Collapsed;

            FolderTree.RootNodes.Clear();

            if (TabViewContainer.ThisPage.HardDeviceList.FirstOrDefault((Item) => Item.Folder.Path == Path.GetPathRoot(Folder.Path)) is HardDeviceInfo Info && Info.DriveType == DriveType.Network)
            {
                IsNetworkDevice = true;
            }
            else
            {
                IsNetworkDevice = false;
            }

            bool HasItem = (await Folder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0;

            StorageFolder ParentFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetPathRoot(Folder.Path));

            TreeViewNode RootNode = new TreeViewNode
            {
                Content = new TreeViewNodeContent(ParentFolder),
                HasUnrealizedChildren = HasItem,
                IsExpanded = HasItem
            };
            FolderTree.RootNodes.Add(RootNode);

            if (HasItem)
            {
                await FillTreeNode(RootNode).ConfigureAwait(true);

                TreeViewNode TargetNode = await RootNode.GetChildNodeAsync(new PathAnalysis(Folder.Path, string.Empty)).ConfigureAwait(true);
                if (TargetNode == null)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else
                {
                    await DisplayItemsInFolder(TargetNode).ConfigureAwait(false);
                }
            }
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is Tuple<Microsoft.UI.Xaml.Controls.TabViewItem, StorageFolder, ThisPC> Parameters)
            {
                string PlaceText = Parameters.Item2.DisplayName.Length > 18 ? Parameters.Item2.DisplayName.Substring(0, 18) + "..." : Parameters.Item2.DisplayName;

                GlobeSearch.PlaceholderText = $"{Globalization.GetString("SearchBox_PlaceholderText")} {PlaceText}";

                if (Parameters.Item1 != null)
                {
                    TabItem = Parameters.Item1;
                }

                if (Parameters.Item3 != null && !TabViewContainer.ThisPage.TFInstanceContainer.ContainsKey(Parameters.Item3))
                {
                    TabViewContainer.ThisPage.TFInstanceContainer.Add(Parameters.Item3, this);
                }

                if (TabViewContainer.ThisPage.HardDeviceList.FirstOrDefault((Item) => Item.Folder.Path == Path.GetPathRoot(Parameters.Item2.Path)) is HardDeviceInfo Info && Info.DriveType == DriveType.Network)
                {
                    IsNetworkDevice = true;
                }
                else
                {
                    IsNetworkDevice = false;
                }

                await Initialize(Parameters.Item2).ConfigureAwait(false);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            while (Nav.CanGoBack)
            {
                Nav.GoBack();
            }

            AddressButtonList.Clear();

            FolderTree.RootNodes.Clear();

            TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.Clear();
            TabViewContainer.ThisPage.FFInstanceContainer[this].HasFile.Visibility = Visibility.Collapsed;

            RecordIndex = 0;

            GoAndBackRecord.Clear();

            IsBackOrForwardAction = false;
            GoBackRecord.IsEnabled = false;
            GoForwardRecord.IsEnabled = false;
            GoParentFolder.IsEnabled = false;

            CurrentNode = null;
            CurrentFolder = null;
        }

        /// <summary>
        /// 执行文件目录的初始化
        /// </summary>
        private async Task Initialize(StorageFolder InitFolder)
        {
            if (InitFolder != null)
            {
                FolderTree.RootNodes.Clear();
                TreeViewNode RootNode = new TreeViewNode
                {
                    Content = new TreeViewNodeContent(InitFolder),
                    IsExpanded = false,
                    HasUnrealizedChildren = (await InitFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                };

                FolderTree.RootNodes.Add(RootNode);

                if (SettingControl.IsDetachTreeViewAndPresenter)
                {
                    await DisplayItemsInFolder(InitFolder).ConfigureAwait(false);
                }
                else
                {
                    await DisplayItemsInFolder(RootNode).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 向特定TreeViewNode节点下添加子节点
        /// </summary>
        /// <param name="Node">节点</param>
        /// <returns></returns>
        public async Task FillTreeNode(TreeViewNode Node)
        {
            if (Node == null)
            {
                throw new ArgumentNullException(nameof(Node), "Parameter could not be null");
            }

            if (Node.Content is TreeViewNodeContent Content)
            {
                try
                {
                    if (IsNetworkDevice)
                    {
                        if (await Content.GetStorageFolderAsync().ConfigureAwait(true) is StorageFolder Folder)
                        {
                            QueryOptions Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                            {
                                FolderDepth = FolderDepth.Shallow,
                                IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                            };
                            Options.SortOrder.Add(new SortEntry { AscendingOrder = true, PropertyName = "System.FileName" });

                            StorageFolderQueryResult Query = Folder.CreateFolderQueryWithOptions(Options);

                            uint ItemCount = await Query.GetItemCountAsync();

                            for (uint i = 0; i < ItemCount && Node.CanTraceToRootNode(FolderTree.RootNodes.FirstOrDefault()); i += 10)
                            {
                                IReadOnlyList<StorageFolder> ItemList = await Query.GetFoldersAsync(i, 10);

                                for (int j = 0; j < ItemList.Count && Node.IsExpanded; j++)
                                {
                                    TreeViewNode NewNode = new TreeViewNode
                                    {
                                        Content = new TreeViewNodeContent(ItemList[j]),
                                        HasUnrealizedChildren = (await ItemList[j].GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0
                                    };

                                    Node.Children.Add(NewNode);
                                }
                            }
                        }
                    }
                    else
                    {
                        List<string> StorageItemPath = WIN_Native_API.GetStorageItemsPath(Content.Path, SettingControl.IsDisplayHiddenItem, ItemFilters.Folder);

                        for (int i = 0; i < StorageItemPath.Count && Node.IsExpanded && Node.CanTraceToRootNode(FolderTree.RootNodes.FirstOrDefault()); i++)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                            {
                                TreeViewNode NewNode = new TreeViewNode
                                {
                                    Content = new TreeViewNodeContent(StorageItemPath[i]),
                                    HasUnrealizedChildren = WIN_Native_API.CheckContainsAnyItem(StorageItemPath[i], ItemFilters.Folder)
                                };

                                Node.Children.Add(NewNode);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    ExceptionTracer.RequestBlueScreen(ex);
                }
                finally
                {
                    if (!Node.IsExpanded)
                    {
                        Node.Children.Clear();
                    }
                }
            }
        }

        private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            await FillTreeNode(args.Node).ConfigureAwait(false);
        }

        private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode Node)
            {
                if (WIN_Native_API.CheckIfHidden((Node.Content as TreeViewNodeContent).Path))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);
                }
                else
                {
                    await CancelAddItemOperation().ConfigureAwait(true);
                    await DisplayItemsInFolder(Node).ConfigureAwait(false);
                }
            }
        }

        public async Task DisplayItemsInFolder(TreeViewNode Node, bool ForceRefresh = false)
        {
            await EnterLock.WaitAsync().ConfigureAwait(true);

            if (Node == null)
            {
                throw new ArgumentNullException(nameof(Node), "Parameter could not be null");
            }

            try
            {
                AddItemCancellation = new CancellationTokenSource();

                if (Node.Content is TreeViewNodeContent Content)
                {
                    while (Nav.CurrentSourcePageType != typeof(FilePresenter))
                    {
                        Nav.GoBack();
                    }

                    if (!ForceRefresh)
                    {
                        if (Content.Path == CurrentFolder?.Path)
                        {
                            return;
                        }
                    }

                    if (IsBackOrForwardAction)
                    {
                        IsBackOrForwardAction = false;
                    }
                    else if (!ForceRefresh)
                    {
                        if (RecordIndex != GoAndBackRecord.Count - 1 && GoAndBackRecord.Count != 0)
                        {
                            GoAndBackRecord.RemoveRange(RecordIndex + 1, GoAndBackRecord.Count - RecordIndex - 1);
                        }

                        GoAndBackRecord.Add(Content.Path);

                        RecordIndex = GoAndBackRecord.Count - 1;
                    }

                    CurrentNode = Node;

                    FilePresenter Presenter = TabViewContainer.ThisPage.FFInstanceContainer[this];

                    Presenter.FileCollection.Clear();

                    if (await Content.GetStorageFolderAsync().ConfigureAwait(true) is StorageFolder Folder)
                    {
                        if (IsNetworkDevice)
                        {
                            QueryOptions Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                            {
                                FolderDepth = FolderDepth.Shallow,
                                IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                            };
                            Options.SortOrder.Add(new SortEntry { AscendingOrder = true, PropertyName = "System.FileName" });
                            Options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new string[] { "System.FileName", "System.DateModified", "System.ItemNameDisplay", "System.Size", "System.ItemTypeText" });
                            Options.SetThumbnailPrefetch(ThumbnailMode.ListView, 100, ThumbnailOptions.UseCurrentScale);

                            StorageItemQueryResult Query = Folder.CreateItemQueryWithOptions(Options);

                            uint ItemCount = await Query.GetItemCountAsync();

                            Presenter.HasFile.Visibility = ItemCount > 0 ? Visibility.Collapsed : Visibility.Visible;

                            for (uint i = 0; i < ItemCount && !AddItemCancellation.IsCancellationRequested; i += 10)
                            {
                                IReadOnlyList<IStorageItem> ItemList = await Query.GetItemsAsync(i, 10);

                                for (int j = 0; j < ItemList.Count && !AddItemCancellation.IsCancellationRequested; j++)
                                {
                                    IStorageItem Item = ItemList[j];
                                    if (Item is StorageFolder ItemFolder)
                                    {
                                        Presenter.FileCollection.Add(new FileSystemStorageItem(ItemFolder, await ItemFolder.GetModifiedTimeAsync().ConfigureAwait(true)));
                                    }
                                    else if (Item is StorageFile ItemFile)
                                    {
                                        Presenter.FileCollection.Add(new FileSystemStorageItem(ItemFile, await ItemFile.GetSizeRawDataAsync().ConfigureAwait(true), await ItemFile.GetThumbnailBitmapAsync().ConfigureAwait(true), await ItemFile.GetModifiedTimeAsync().ConfigureAwait(true)));
                                    }
                                }
                            }
                        }
                        else
                        {
                            List<FileSystemStorageItem> ItemList = SortCollectionGenerator.Current.GetSortedCollection(WIN_Native_API.GetStorageItems(Content.Path, SettingControl.IsDisplayHiddenItem, ItemFilters.File | ItemFilters.Folder));

                            Presenter.HasFile.Visibility = ItemList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                            for (int i = 0; i < ItemList.Count; i++)
                            {
                                TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.Add(ItemList[i]);
                            }
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
                AddItemCancellation.Dispose();
                AddItemCancellation = null;

                EnterLock.Release();
            }
        }

        public async Task DisplayItemsInFolder(StorageFolder Folder, bool ForceRefresh = false)
        {
            await EnterLock.WaitAsync().ConfigureAwait(true);

            if (Folder == null)
            {
                throw new ArgumentNullException(nameof(Folder), "Parameter could not be null");
            }

            try
            {
                AddItemCancellation = new CancellationTokenSource();

                while (Nav.CurrentSourcePageType != typeof(FilePresenter))
                {
                    Nav.GoBack();
                }

                if (!ForceRefresh)
                {
                    if (Folder.Path == CurrentFolder?.Path)
                    {
                        return;
                    }
                }

                if (IsBackOrForwardAction)
                {
                    IsBackOrForwardAction = false;
                }
                else if (!ForceRefresh)
                {
                    if (RecordIndex != GoAndBackRecord.Count - 1 && GoAndBackRecord.Count != 0)
                    {
                        GoAndBackRecord.RemoveRange(RecordIndex + 1, GoAndBackRecord.Count - RecordIndex - 1);
                    }

                    GoAndBackRecord.Add(Folder.Path);

                    RecordIndex = GoAndBackRecord.Count - 1;
                }

                CurrentFolder = Folder;

                FilePresenter Presenter = TabViewContainer.ThisPage.FFInstanceContainer[this];

                Presenter.FileCollection.Clear();

                if (IsNetworkDevice)
                {
                    QueryOptions Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                    {
                        FolderDepth = FolderDepth.Shallow,
                        IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                    };
                    Options.SortOrder.Add(new SortEntry { AscendingOrder = true, PropertyName = "System.FileName" });
                    Options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new string[] { "System.FileName", "System.DateModified", "System.ItemNameDisplay", "System.Size", "System.ItemTypeText" });
                    Options.SetThumbnailPrefetch(ThumbnailMode.ListView, 100, ThumbnailOptions.UseCurrentScale);

                    StorageItemQueryResult Query = Folder.CreateItemQueryWithOptions(Options);

                    uint ItemCount = await Query.GetItemCountAsync();

                    Presenter.HasFile.Visibility = ItemCount > 0 ? Visibility.Collapsed : Visibility.Visible;

                    for (uint i = 0; i < ItemCount && !AddItemCancellation.IsCancellationRequested; i += 10)
                    {
                        IReadOnlyList<IStorageItem> ItemList = await Query.GetItemsAsync(i, 10);

                        for (int j = 0; j < ItemList.Count && !AddItemCancellation.IsCancellationRequested; j++)
                        {
                            IStorageItem Item = ItemList[j];
                            if (Item is StorageFolder ItemFolder)
                            {
                                Presenter.FileCollection.Add(new FileSystemStorageItem(ItemFolder, await ItemFolder.GetModifiedTimeAsync().ConfigureAwait(true)));
                            }
                            else if (Item is StorageFile ItemFile)
                            {
                                Presenter.FileCollection.Add(new FileSystemStorageItem(ItemFile, await ItemFile.GetSizeRawDataAsync().ConfigureAwait(true), await ItemFile.GetThumbnailBitmapAsync().ConfigureAwait(true), await ItemFile.GetModifiedTimeAsync().ConfigureAwait(true)));
                            }
                        }
                    }
                }
                else
                {
                    List<FileSystemStorageItem> ItemList = SortCollectionGenerator.Current.GetSortedCollection(WIN_Native_API.GetStorageItems(Folder, SettingControl.IsDisplayHiddenItem, ItemFilters.File | ItemFilters.Folder));

                    Presenter.HasFile.Visibility = ItemList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                    for (int i = 0; i < ItemList.Count; i++)
                    {
                        Presenter.FileCollection.Add(ItemList[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionTracer.RequestBlueScreen(ex);
            }
            finally
            {
                AddItemCancellation.Dispose();
                AddItemCancellation = null;

                EnterLock.Release();
            }
        }

        private async void FolderDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            DeleteDialog QueueContenDialog = new DeleteDialog(Globalization.GetString("QueueDialog_DeleteFolder_Content"));

            if (await QueueContenDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
            {
                await LoadingActivation(true, Globalization.GetString("Progress_Tip_Deleting")).ConfigureAwait(true);

                try
                {
                    await FullTrustExcutorController.Current.DeleteAsync(CurrentFolder, QueueContenDialog.IsPermanentDelete).ConfigureAwait(true);

                    if (IsNetworkDevice)
                    {
                        TreeViewNode ParentNode = CurrentNode.Parent;

                        ParentNode.Children.Remove(CurrentNode);

                        if (ParentNode.Children.Count == 0)
                        {
                            ParentNode.HasUnrealizedChildren = false;
                        }

                        await DisplayItemsInFolder(ParentNode).ConfigureAwait(true);
                    }
                    else
                    {
                        await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);

                        await CurrentNode.UpdateAllSubNodeAsync().ConfigureAwait(true);
                    }
                }
                catch (FileCaputureException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_Item_Captured_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                catch (FileNotFoundException)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_RefreshButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(true);

                    await DisplayItemsInFolder(CurrentNode.Parent).ConfigureAwait(true);
                }
                catch (InvalidOperationException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteFailUnexpectError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                catch (Exception)
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_DeleteItemError_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }

                await LoadingActivation(false).ConfigureAwait(true);
            }
        }

        private void FolderTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            args.Node.Children.Clear();
        }

        private async void FolderTree_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is TreeViewNode Node)
                {
                    if (WIN_Native_API.CheckIfHidden((Node.Content as TreeViewNodeContent).Path))
                    {
                        FolderTree.ContextFlyout = null;
                    }
                    else
                    {
                        FolderTree.ContextFlyout = RightTabFlyout;

                        await DisplayItemsInFolder(Node).ConfigureAwait(true);

                        if (FolderTree.RootNodes.Contains(CurrentNode))
                        {
                            FolderDelete.IsEnabled = false;
                            FolderRename.IsEnabled = false;
                            FolderAdd.IsEnabled = false;
                        }
                        else
                        {
                            FolderDelete.IsEnabled = true;
                            FolderRename.IsEnabled = true;
                            FolderAdd.IsEnabled = true;
                        }
                    }
                }
                else
                {
                    FolderTree.ContextFlyout = null;
                }
            }
        }

        private async void FolderRename_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            RenameDialog renameDialog = new RenameDialog(CurrentFolder);
            if (await renameDialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
            {
                if (string.IsNullOrEmpty(renameDialog.DesireName))
                {
                    QueueContentDialog content = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_EmptyFolderName_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await content.ShowAsync().ConfigureAwait(true);
                    return;
                }

                try
                {
                    await CurrentFolder.RenameAsync(renameDialog.DesireName);

                    (CurrentNode.Content as TreeViewNodeContent).Update(CurrentFolder);

                    UpdateAddressButton(CurrentFolder.Path);
                }
                catch (UnauthorizedAccessException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_UnauthorizedRenameFolder_Content"),
                        PrimaryButtonText = Globalization.GetString("Common_Dialog_NowButton"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_LaterButton"),
                    };

                    if (await dialog.ShowAsync().ConfigureAwait(true) == ContentDialogResult.Primary)
                    {
                        _ = await Launcher.LaunchFolderAsync(CurrentFolder);
                    }
                }
                catch (FileLoadException)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_FolderOccupied_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };

                    _ = await dialog.ShowAsync().ConfigureAwait(true);
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
                        await CurrentFolder.RenameAsync(renameDialog.DesireName, NameCollisionOption.GenerateUniqueName);

                        await CurrentNode.Parent.UpdateAllSubNodeAsync().ConfigureAwait(true);

                        UpdateAddressButton(CurrentFolder.Path);
                    }
                }
            }
        }

        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync().ConfigureAwait(true);
                return;
            }

            try
            {
                StorageFolder NewFolder = await CurrentFolder.CreateFolderAsync(Globalization.GetString("Create_NewFolder_Admin_Name"), CreationCollisionOption.GenerateUniqueName);

                if (IsNetworkDevice)
                {
                    if (CurrentNode.IsExpanded)
                    {
                        CurrentNode.Children.Add(new TreeViewNode
                        {
                            Content = new TreeViewNodeContent(NewFolder),
                            HasUnrealizedChildren = false
                        });

                        TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.Add(new FileSystemStorageItem(NewFolder, await NewFolder.GetModifiedTimeAsync().ConfigureAwait(true)));
                    }
                }
            }
            catch (UnauthorizedAccessException)
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
                    _ = await Launcher.LaunchFolderAsync(CurrentFolder);
                }
            }
        }

        private async void FolderAttribute_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if (CurrentNode == FolderTree.RootNodes.FirstOrDefault())
            {
                if (TabViewContainer.ThisPage.HardDeviceList.FirstOrDefault((Device) => Device.Name == CurrentFolder.DisplayName) is HardDeviceInfo Info)
                {
                    DeviceInfoDialog dialog = new DeviceInfoDialog(Info);
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
                else
                {
                    PropertyDialog Dialog = new PropertyDialog(CurrentFolder);
                    _ = await Dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                PropertyDialog Dialog = new PropertyDialog(CurrentFolder);
                _ = await Dialog.ShowAsync().ConfigureAwait(true);
            }
        }

        private async void FolderAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!await CurrentFolder.CheckExist().ConfigureAwait(true))
            {
                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };
                _ = await dialog.ShowAsync().ConfigureAwait(true);

                return;
            }

            if (TabViewContainer.ThisPage.LibraryFolderList.Any((Folder) => Folder.Folder.Path == CurrentFolder.Path))
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
                BitmapImage Thumbnail = await CurrentFolder.GetThumbnailBitmapAsync().ConfigureAwait(true);
                TabViewContainer.ThisPage.LibraryFolderList.Add(new LibraryFolder(CurrentFolder, Thumbnail));
                await SQLite.Current.SetLibraryPathAsync(CurrentFolder.Path, LibraryType.UserCustom).ConfigureAwait(false);
            }
        }

        private async void GlobeSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.QueryText))
            {
                return;
            }

            FlyoutBase.ShowAttachedFlyout(sender);

            await SQLite.Current.SetSearchHistoryAsync(args.QueryText).ConfigureAwait(false);
        }

        private async void GlobeSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(sender.Text))
            {
                if (Nav.CurrentSourcePageType == typeof(SearchPage))
                {
                    Nav.GoBack();
                }
                return;
            }

            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                sender.ItemsSource = await SQLite.Current.GetRelatedSearchHistoryAsync(sender.Text).ConfigureAwait(true);
            }
        }

        private void SearchConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchFlyout.Hide();

                if (ApplicationData.Current.LocalSettings.Values["LaunchSearchTips"] == null)
                {
                    ApplicationData.Current.LocalSettings.Values["LaunchSearchTips"] = true;
                    SearchTip.IsOpen = true;
                }

                QueryOptions Options;
                if (ShallowRadio.IsChecked.GetValueOrDefault())
                {
                    Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                    {
                        FolderDepth = FolderDepth.Shallow,
                        IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                        ApplicationSearchFilter = "System.FileName:*" + GlobeSearch.Text + "*"
                    };
                }
                else
                {
                    Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                    {
                        FolderDepth = FolderDepth.Deep,
                        IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                        ApplicationSearchFilter = "System.FileName:*" + GlobeSearch.Text + "*"
                    };
                }

                Options.SetThumbnailPrefetch(ThumbnailMode.ListView, 100, ThumbnailOptions.ResizeThumbnail);
                Options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new string[] { "System.ItemTypeText", "System.ItemNameDisplayWithoutExtension", "System.FileName", "System.Size", "System.DateModified" });

                if (Nav.CurrentSourcePageType.Name != "SearchPage")
                {
                    StorageItemQueryResult FileQuery = CurrentFolder.CreateItemQueryWithOptions(Options);

                    if (AnimationController.Current.IsEnableAnimation)
                    {
                        Nav.Navigate(typeof(SearchPage), new Tuple<FileControl, StorageItemQueryResult>(this, FileQuery), new DrillInNavigationTransitionInfo());
                    }
                    else
                    {
                        Nav.Navigate(typeof(SearchPage), new Tuple<FileControl, StorageItemQueryResult>(this, FileQuery), new SuppressNavigationTransitionInfo());
                    }
                }
                else
                {
                    TabViewContainer.ThisPage.FSInstanceContainer[this].SetSearchTarget = Options;
                }
            }
            catch (Exception ex)
            {
                ExceptionTracer.RequestBlueScreen(ex);
            }
        }

        private void SearchCancel_Click(object sender, RoutedEventArgs e)
        {
            SearchFlyout.Hide();
        }

        private void SearchFlyout_Opened(object sender, object e)
        {
            _ = SearchConfirm.Focus(FocusState.Programmatic);
        }

        private async void GlobeSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            IsSearchOrPathBoxFocused = true;
            if (string.IsNullOrEmpty(GlobeSearch.Text))
            {
                GlobeSearch.ItemsSource = await SQLite.Current.GetRelatedSearchHistoryAsync(string.Empty).ConfigureAwait(true);
            }
        }

        private async void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            LoadingControl.Focus(FocusState.Programmatic);

            string QueryText = string.Empty;
            if (args.ChosenSuggestion == null)
            {
                if (string.IsNullOrEmpty(AddressBoxTextBackup))
                {
                    return;
                }
                else
                {
                    QueryText = AddressBoxTextBackup;
                }
            }
            else
            {
                QueryText = args.ChosenSuggestion.ToString();
            }

            if (QueryText == CurrentFolder.Path)
            {
                return;
            }

            if (string.Equals(QueryText, "Powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                string ExcutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
                await FullTrustExcutorController.Current.RunAsAdministratorAsync(ExcutePath, $"-NoExit -Command \"Set-Location '{CurrentFolder.Path}'\"").ConfigureAwait(false);
                return;
            }

            if (string.Equals(QueryText, "Cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                string ExcutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                await FullTrustExcutorController.Current.RunAsAdministratorAsync(ExcutePath, $"/k cd /d {CurrentFolder.Path}").ConfigureAwait(false);
                return;
            }

            if (string.Equals(QueryText, "Wt", StringComparison.OrdinalIgnoreCase) || string.Equals(QueryText, "Wt.exe", StringComparison.OrdinalIgnoreCase))
            {
                LaunchQuerySupportStatus CheckResult = await Launcher.QueryUriSupportAsync(new Uri("ms-windows-store:"), LaunchQuerySupportType.Uri, "Microsoft.WindowsTerminal_8wekyb3d8bbwe");
                switch (CheckResult)
                {
                    case LaunchQuerySupportStatus.Available:
                    case LaunchQuerySupportStatus.NotSupported:
                        {
                            await FullTrustExcutorController.Current.RunAsync("wt.exe", $"/d {CurrentFolder.Path}").ConfigureAwait(false);
                            return;
                        }
                }
            }

            string ProtentialPath1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), QueryText.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? QueryText.ToLower() : $"{QueryText.ToLower()}.exe");
            string ProtentialPath2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), QueryText.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? QueryText.ToLower() : $"{QueryText.ToLower()}.exe");
            string ProtentialPath3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), QueryText.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? QueryText.ToLower() : $"{QueryText.ToLower()}.exe");

            if (WIN_Native_API.CheckExist(ProtentialPath1))
            {
                await FullTrustExcutorController.Current.RunAsAdministratorAsync(ProtentialPath1, string.Empty).ConfigureAwait(false);
                return;
            }
            else if (WIN_Native_API.CheckExist(ProtentialPath2))
            {
                await FullTrustExcutorController.Current.RunAsAdministratorAsync(ProtentialPath2, string.Empty).ConfigureAwait(false);
                return;
            }
            else if (WIN_Native_API.CheckExist(ProtentialPath3))
            {
                await FullTrustExcutorController.Current.RunAsAdministratorAsync(ProtentialPath3, string.Empty).ConfigureAwait(false);
                return;
            }

            try
            {
                if (WIN_Native_API.CheckIfHidden(QueryText))
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

                if (Path.IsPathRooted(QueryText) && TabViewContainer.ThisPage.HardDeviceList.Any((Drive) => Drive.Folder.Path == Path.GetPathRoot(QueryText)))
                {
                    StorageFile File = await StorageFile.GetFileFromPathAsync(QueryText);
                    if (!await Launcher.LaunchFileAsync(File))
                    {
                        LauncherOptions options = new LauncherOptions
                        {
                            DisplayApplicationPicker = true
                        };
                        _ = await Launcher.LaunchFileAsync(File, options);
                    }
                }
                else
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{QueryText}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                try
                {
                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(QueryText);

                    if (SettingControl.IsDetachTreeViewAndPresenter)
                    {
                        await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                    }
                    else
                    {
                        if (QueryText.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                        {
                            TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);
                            if (TargetNode != null)
                            {
                                await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }
                        else
                        {
                            await OpenTargetFolder(Folder).ConfigureAwait(true);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{QueryText}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
        }

        private void AddressBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            AddressBoxTextBackup = sender.Text;

            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (Path.IsPathRooted(sender.Text)
                    && Path.GetDirectoryName(sender.Text) is string DirectoryName
                    && TabViewContainer.ThisPage.HardDeviceList.Any((Drive) => Drive.Folder.Path == Path.GetPathRoot(sender.Text)))
                {
                    if (Interlocked.Exchange(ref TextChangeLockResource, 1) == 0)
                    {
                        try
                        {
                            if (args.CheckCurrent())
                            {
                                sender.ItemsSource = WIN_Native_API.GetStorageItems(DirectoryName, false, ItemFilters.Folder).Where((Item) => Item.Name.StartsWith(Path.GetFileName(sender.Text), StringComparison.OrdinalIgnoreCase)).Select((It) => It.Path);
                            }
                            else
                            {
                                sender.ItemsSource = null;
                            }
                        }
                        catch (Exception)
                        {
                            sender.ItemsSource = null;
                        }
                        finally
                        {
                            _ = Interlocked.Exchange(ref TextChangeLockResource, 0);
                        }
                    }
                }
            }
        }

        private async void GoParentFolder_Click(object sender, RoutedEventArgs e)
        {
            await CancelAddItemOperation().ConfigureAwait(true);

            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                try
                {
                    if (WIN_Native_API.CheckIfHidden(Path.GetDirectoryName(CurrentFolder.Path)))
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

                    if (SettingControl.IsDetachTreeViewAndPresenter)
                    {
                        if ((await CurrentFolder.GetParentAsync()) is StorageFolder ParentFolder)
                        {
                            await DisplayItemsInFolder(ParentFolder).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        if ((await CurrentFolder.GetParentAsync()) is StorageFolder ParentFolder)
                        {
                            TreeViewNode ParentNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ParentFolder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);

                            if (ParentFolder != null)
                            {
                                await DisplayItemsInFolder(ParentNode).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        public async void GoBackRecord_Click(object sender, RoutedEventArgs e)
        {
            await CancelAddItemOperation().ConfigureAwait(true);

            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                string Path = GoAndBackRecord[--RecordIndex];

                if (WIN_Native_API.CheckIfHidden(Path))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);

                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                    return;
                }

                try
                {
                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(Path);

                    IsBackOrForwardAction = true;
                    if (SettingControl.IsDetachTreeViewAndPresenter)
                    {
                        await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                    }
                    else
                    {
                        if (Path.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                        {

                            TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);
                            if (TargetNode == null)
                            {
                                QueueContentDialog dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };
                                _ = await dialog.ShowAsync().ConfigureAwait(true);
                            }
                            else
                            {
                                await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }
                        else
                        {
                            await OpenTargetFolder(Folder).ConfigureAwait(false);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);

                    RecordIndex++;
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        public async void GoForwardRecord_Click(object sender, RoutedEventArgs e)
        {
            await CancelAddItemOperation().ConfigureAwait(true);

            if (Interlocked.Exchange(ref NavigateLockResource, 1) == 0)
            {
                string Path = GoAndBackRecord[++RecordIndex];

                if (WIN_Native_API.CheckIfHidden(Path))
                {
                    QueueContentDialog Dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_ItemHidden_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await Dialog.ShowAsync().ConfigureAwait(false);

                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);

                    return;
                }

                try
                {
                    StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(Path);

                    IsBackOrForwardAction = true;
                    if (SettingControl.IsDetachTreeViewAndPresenter)
                    {
                        await DisplayItemsInFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                    }
                    else
                    {
                        if (Path.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                        {

                            TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(Folder.Path, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);
                            if (TargetNode == null)
                            {
                                QueueContentDialog dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };
                                _ = await dialog.ShowAsync().ConfigureAwait(true);
                            }
                            else
                            {
                                await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                                await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                            }
                        }
                        else
                        {
                            await OpenTargetFolder(Folder).ConfigureAwait(true);

                            await SQLite.Current.SetPathHistoryAsync(Folder.Path).ConfigureAwait(true);
                        }
                    }
                }
                catch (Exception)
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{Path}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);

                    RecordIndex--;
                }
                finally
                {
                    _ = Interlocked.Exchange(ref NavigateLockResource, 0);
                }
            }
        }

        private async void AddressBox_GotFocus(object sender, RoutedEventArgs e)
        {
            IsSearchOrPathBoxFocused = true;

            if (string.IsNullOrEmpty(AddressBox.Text))
            {
                AddressBox.Text = CurrentFolder.Path;
            }
            AddressButtonScrollViewer.Visibility = Visibility.Collapsed;

            AddressBox.ItemsSource = await SQLite.Current.GetRelatedPathHistoryAsync().ConfigureAwait(true);
        }

        private void ItemDisplayMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["FilePresenterDisplayMode"] = ItemDisplayMode.SelectedIndex;

            switch (ItemDisplayMode.SelectedIndex)
            {
                case 0:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].TileDataTemplate;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl;
                        break;
                    }
                case 1:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListHeader.ContentTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].ListHeaderDataTemplate;
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewDetailDataTemplate;
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl.ItemsSource = TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl;
                        break;
                    }

                case 2:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListHeader.ContentTemplate = null;
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewSimpleDataTemplate;
                        TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl.ItemsSource = TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].ListViewControl;
                        break;
                    }
                case 3:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].LargeImageDataTemplate;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl;
                        break;
                    }
                case 4:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].MediumImageDataTemplate;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl;
                        break;
                    }
                case 5:
                    {
                        TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl.ItemTemplate = TabViewContainer.ThisPage.FFInstanceContainer[this].SmallImageDataTemplate;

                        TabViewContainer.ThisPage.FFInstanceContainer[this].ItemPresenter = TabViewContainer.ThisPage.FFInstanceContainer[this].GridViewControl;
                        break;
                    }
            }
        }

        private void AddressBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Tab)
            {
                string FirstTip = AddressBox.Items.FirstOrDefault()?.ToString();

                if (!string.IsNullOrEmpty(FirstTip))
                {
                    AddressBox.Text = FirstTip;
                }

                e.Handled = true;
            }
        }

        private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AddressBox.Text = string.Empty;
            AddressButtonScrollViewer.Visibility = Visibility.Visible;
        }

        private async void AddressButton_Click(object sender, RoutedEventArgs e)
        {
            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(((Button)sender).DataContext as AddressBlock) + 1).Skip(1));
            string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            if (ActualString == CurrentFolder.Path)
            {
                return;
            }

            if (WIN_Native_API.CheckIfHidden(ActualString))
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

            if (SettingControl.IsDetachTreeViewAndPresenter)
            {
                try
                {
                    StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(ActualString);
                    await DisplayItemsInFolder(TargetFolder).ConfigureAwait(true);
                    await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                }
                catch
                {
                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                    };
                    _ = await dialog.ShowAsync().ConfigureAwait(true);
                }
            }
            else
            {
                if (ActualString.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                {
                    if ((await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ActualString, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true)) is TreeViewNode TargetNode)
                    {
                        await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                    }
                    else
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
                else
                {
                    try
                    {
                        StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(ActualString);

                        await OpenTargetFolder(Folder).ConfigureAwait(true);

                        await SQLite.Current.SetPathHistoryAsync(ActualString).ConfigureAwait(true);
                    }
                    catch
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{ActualString}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
            }
        }

        private async void AddressExtention_Click(object sender, RoutedEventArgs e)
        {
            Button Btn = sender as Button;
            TextBlock StateText = Btn.Content as TextBlock;

            AddressExtentionList.Clear();

            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(Btn.DataContext as AddressBlock) + 1).Skip(1));
            string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            if (IsNetworkDevice)
            {
                StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(ActualString);

                QueryOptions Options = new QueryOptions(CommonFolderQuery.DefaultQuery)
                {
                    FolderDepth = FolderDepth.Shallow,
                    IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                };
                Options.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new string[] { "System.FileName" });
                Options.SortOrder.Add(new SortEntry()
                {
                    AscendingOrder = true,
                    PropertyName = "System.FileName"
                });

                StorageFolderQueryResult Query = Folder.CreateFolderQueryWithOptions(Options);

                foreach (string SubFolderName in (await Query.GetFoldersAsync(0, 100)).Select((SubFolder) => SubFolder.Name))
                {
                    AddressExtentionList.Add(SubFolderName);
                }
            }
            else
            {
                List<string> ItemList = WIN_Native_API.GetStorageItemsPath(ActualString, SettingControl.IsDisplayHiddenItem, ItemFilters.Folder);

                foreach (string SubFolderName in ItemList.Select((Item) => Path.GetFileName(Item)))
                {
                    AddressExtentionList.Add(SubFolderName);
                }
            }

            if (AddressExtentionList.Count != 0)
            {
                StateText.RenderTransformOrigin = new Point(0.55, 0.6);
                await StateText.Rotate(90, duration: 150).StartAsync().ConfigureAwait(true);

                FlyoutBase.SetAttachedFlyout(Btn, AddressExtentionFlyout);
                FlyoutBase.ShowAttachedFlyout(Btn);
            }
        }

        private async void AddressExtentionFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
        {
            AddressExtentionList.Clear();

            await ((sender.Target as Button).Content as FrameworkElement).Rotate(0, duration: 150).StartAsync().ConfigureAwait(false);
        }

        private async void AddressExtensionSubFolderList_ItemClick(object sender, ItemClickEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AddressExtentionFlyout.Hide();
            });

            if (!string.IsNullOrEmpty(e.ClickedItem.ToString()))
            {
                string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(AddressExtentionFlyout.Target.DataContext as AddressBlock) + 1).Skip(1));
                string ActualString = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

                string TargetPath = Path.Combine(ActualString, e.ClickedItem.ToString());

                if (WIN_Native_API.CheckIfHidden(TargetPath))
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

                if (SettingControl.IsDetachTreeViewAndPresenter)
                {
                    try
                    {
                        StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(TargetPath);

                        await DisplayItemsInFolder(TargetFolder).ConfigureAwait(true);
                    }
                    catch
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                            Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                        };
                        _ = await dialog.ShowAsync().ConfigureAwait(true);
                    }
                }
                else
                {
                    if (TargetPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                    {
                        TreeViewNode TargetNode = await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(TargetPath, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path)).ConfigureAwait(true);
                        if (TargetNode != null)
                        {
                            await DisplayItemsInFolder(TargetNode).ConfigureAwait(true);
                        }
                        else
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                    else
                    {
                        try
                        {
                            StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(TargetPath);

                            await OpenTargetFolder(Folder).ConfigureAwait(true);
                        }
                        catch
                        {
                            QueueContentDialog dialog = new QueueContentDialog
                            {
                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                Content = $"{Globalization.GetString("QueueDialog_LocatePathFailure_Content")} \r\"{TargetPath}\"",
                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton"),
                            };
                            _ = await dialog.ShowAsync().ConfigureAwait(true);
                        }
                    }
                }
            }
        }

        private async void AddressButton_Drop(object sender, DragEventArgs e)
        {
            string OriginalString = string.Join("\\", AddressButtonList.Take(AddressButtonList.IndexOf(((Button)sender).DataContext as AddressBlock) + 1).Skip(1));
            string ActualPath = Path.Combine(Path.GetPathRoot(CurrentFolder.Path), OriginalString);

            bool IsHiddenTarget = false;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IsHiddenTarget = WIN_Native_API.CheckIfHidden(ActualPath);
            });

            if (IsHiddenTarget)
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

            if (Interlocked.Exchange(ref DropLockResource, 1) == 0)
            {
                try
                {
                    if (e.DataView.Contains(StandardDataFormats.StorageItems))
                    {
                        List<IStorageItem> DragItemList = (await e.DataView.GetStorageItemsAsync()).ToList();

                        StorageFolder TargetFolder = await StorageFolder.GetFolderFromPathAsync(ActualPath);

                        switch (e.AcceptedOperation)
                        {
                            case DataPackageOperation.Copy:
                                {
                                    await LoadingActivation(true, Globalization.GetString("Progress_Tip_Copying")).ConfigureAwait(true);

                                    bool IsItemNotFound = false;
                                    bool IsUnauthorized = false;
                                    bool IsOperateFailed = false;

                                    try
                                    {
                                        if (IsNetworkDevice)
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

                                                    if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                                    {
                                                        if (await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ActualPath, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path), true).ConfigureAwait(true) is TreeViewNode TargetNode)
                                                        {
                                                            TargetNode.HasUnrealizedChildren = true;

                                                            if (TargetNode.IsExpanded)
                                                            {
                                                                TargetNode.Children.Add(new TreeViewNode
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
                                            await FullTrustExcutorController.Current.CopyAsync(DragItemList, TargetFolder, (s, arg) =>
                                            {
                                                ProBar.IsIndeterminate = false;
                                                ProBar.Value = arg.ProgressPercentage;
                                            }).ConfigureAwait(true);

                                            if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                            {
                                                await FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
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
                                            _ = await Launcher.LaunchFolderAsync(CurrentFolder);
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
                                    if (DragItemList.Select((Item) => Item.Path).All((Item) => Path.GetDirectoryName(Item) == ActualPath))
                                    {
                                        return;
                                    }

                                    await LoadingActivation(true, Globalization.GetString("Progress_Tip_Moving")).ConfigureAwait(true);

                                    bool IsItemNotFound = false;
                                    bool IsUnauthorized = false;
                                    bool IsCaptured = false;
                                    bool IsOperateFailed = false;

                                    try
                                    {
                                        if (IsNetworkDevice)
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

                                                    if (TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.FirstOrDefault((Item) => Item.Path == Folder.Path) is FileSystemStorageItem RemoveItem)
                                                    {
                                                        TabViewContainer.ThisPage.FFInstanceContainer[this].FileCollection.Remove(RemoveItem);
                                                    }

                                                    if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                                    {
                                                        if (CurrentNode.IsExpanded)
                                                        {
                                                            if (CurrentNode.Children.FirstOrDefault((Node) => (Node.Content as TreeViewNodeContent).Path == Folder.Path) is TreeViewNode Node)
                                                            {
                                                                CurrentNode.Children.Remove(Node);
                                                            }
                                                        }

                                                        CurrentNode.HasUnrealizedChildren = (await CurrentFolder.GetFoldersAsync(CommonFolderQuery.DefaultQuery, 0, 1)).Count > 0;

                                                        if (await FolderTree.RootNodes[0].GetChildNodeAsync(new PathAnalysis(ActualPath, (FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path), true).ConfigureAwait(true) is TreeViewNode TargetNode)
                                                        {
                                                            TargetNode.HasUnrealizedChildren = true;

                                                            if (TargetNode.IsExpanded)
                                                            {
                                                                TargetNode.Children.Add(new TreeViewNode
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
                                                ProBar.IsIndeterminate = false;
                                                ProBar.Value = arg.ProgressPercentage;
                                            }).ConfigureAwait(true);

                                            if (!SettingControl.IsDetachTreeViewAndPresenter && ActualPath.StartsWith((FolderTree.RootNodes[0].Content as TreeViewNodeContent).Path))
                                            {
                                                await FolderTree.RootNodes[0].UpdateAllSubNodeAsync().ConfigureAwait(true);
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
                                            _ = await Launcher.LaunchFolderAsync(CurrentFolder);
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
                    await LoadingActivation(false).ConfigureAwait(true);
                    e.Handled = true;
                    _ = Interlocked.Exchange(ref DropLockResource, 0);
                }
            }
        }

        private void AddressButton_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                if (e.Modifiers.HasFlag(DragDropModifiers.Control))
                {
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_CopyTo")} {(e.OriginalSource as Button).Content}";
                }
                else
                {
                    e.AcceptedOperation = DataPackageOperation.Move;
                    e.DragUIOverride.Caption = $"{Globalization.GetString("Drag_Tip_MoveTo")} {(e.OriginalSource as Button).Content}";
                }

                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private async void FolderTree_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is TreeViewNode Node)
                {
                    FolderTree.ContextFlyout = RightTabFlyout;

                    await DisplayItemsInFolder(Node).ConfigureAwait(true);

                    if (FolderTree.RootNodes.Contains(CurrentNode))
                    {
                        FolderDelete.IsEnabled = false;
                        FolderRename.IsEnabled = false;
                        FolderAdd.IsEnabled = false;
                    }
                    else
                    {
                        FolderDelete.IsEnabled = true;
                        FolderRename.IsEnabled = true;
                        FolderAdd.IsEnabled = true;
                    }
                }
                else
                {
                    FolderTree.ContextFlyout = null;
                }
            }
        }

        private void BottomCommandBar_Opening(object sender, object e)
        {
            BottomCommandBar.PrimaryCommands.Clear();
            BottomCommandBar.SecondaryCommands.Clear();

            FilePresenter Instance = TabViewContainer.ThisPage.FFInstanceContainer[this];

            if (Instance.SelectedItems.Count > 1)
            {
                if (Instance.SelectedItems.Any((Item) => Item.IsHidenItem))
                {
                    BottomCommandBar.IsOpen = false;
                    return;
                }

                AppBarButton CopyButton = new AppBarButton
                {
                    Icon = new SymbolIcon(Symbol.Copy),
                    Label = Globalization.GetString("Operate_Text_Copy")
                };
                CopyButton.Click += Instance.Copy_Click;
                BottomCommandBar.PrimaryCommands.Add(CopyButton);

                AppBarButton CutButton = new AppBarButton
                {
                    Icon = new SymbolIcon(Symbol.Cut),
                    Label = Globalization.GetString("Operate_Text_Cut")
                };
                CutButton.Click += Instance.Cut_Click;
                BottomCommandBar.PrimaryCommands.Add(CutButton);

                AppBarButton DeleteButton = new AppBarButton
                {
                    Icon = new SymbolIcon(Symbol.Delete),
                    Label = Globalization.GetString("Operate_Text_Delete")
                };
                DeleteButton.Click += Instance.Delete_Click;
                BottomCommandBar.PrimaryCommands.Add(DeleteButton);

                bool EnableMixZipButton = true;
                string MixZipButtonText = Globalization.GetString("Operate_Text_Compression");

                if (Instance.SelectedItems.Any((Item) => Item.StorageType != StorageItemTypes.Folder))
                {
                    if (Instance.SelectedItems.All((Item) => Item.StorageType == StorageItemTypes.File))
                    {
                        if (Instance.SelectedItems.All((Item) => Item.Type == ".zip"))
                        {
                            MixZipButtonText = Globalization.GetString("Operate_Text_Decompression");
                        }
                        else if (Instance.SelectedItems.All((Item) => Item.Type != ".zip"))
                        {
                            MixZipButtonText = Globalization.GetString("Operate_Text_Compression");
                        }
                        else
                        {
                            EnableMixZipButton = false;
                        }
                    }
                    else
                    {
                        if (Instance.SelectedItems.Where((It) => It.StorageType == StorageItemTypes.File).Any((Item) => Item.Type == ".zip"))
                        {
                            EnableMixZipButton = false;
                        }
                        else
                        {
                            MixZipButtonText = Globalization.GetString("Operate_Text_Compression");
                        }
                    }
                }
                else
                {
                    MixZipButtonText = Globalization.GetString("Operate_Text_Compression");
                }

                AppBarButton CompressionButton = new AppBarButton
                {
                    Icon = new SymbolIcon(Symbol.Bookmarks),
                    Label = MixZipButtonText,
                    IsEnabled = EnableMixZipButton
                };
                CompressionButton.Click += Instance.MixZip_Click;
                BottomCommandBar.SecondaryCommands.Add(CompressionButton);
            }
            else
            {
                if (Instance.SelectedItem is FileSystemStorageItem Item)
                {
                    if (Item.IsHidenItem)
                    {
                        AppBarButton WinExButton = new AppBarButton
                        {
                            Icon = new FontIcon { Glyph = "\uEC50" },
                            Label = Globalization.GetString("Operate_Text_OpenInWinExplorer")
                        };
                        WinExButton.Click += Instance.OpenHiddenItemExplorer_Click;
                        BottomCommandBar.PrimaryCommands.Add(WinExButton);

                        AppBarButton RemoveHiddenButton = new AppBarButton
                        {
                            Icon = new FontIcon { Glyph = "\uF5EF" },
                            Label = Globalization.GetString("Operate_Text_RemoveHidden")
                        };
                        RemoveHiddenButton.Click += Instance.RemoveHidden_Click;
                        BottomCommandBar.PrimaryCommands.Add(RemoveHiddenButton);
                    }
                    else
                    {
                        AppBarButton CopyButton = new AppBarButton
                        {
                            Icon = new SymbolIcon(Symbol.Copy),
                            Label = Globalization.GetString("Operate_Text_Copy")
                        };
                        CopyButton.Click += Instance.Copy_Click;
                        BottomCommandBar.PrimaryCommands.Add(CopyButton);

                        AppBarButton CutButton = new AppBarButton
                        {
                            Icon = new SymbolIcon(Symbol.Cut),
                            Label = Globalization.GetString("Operate_Text_Cut")
                        };
                        CutButton.Click += Instance.Cut_Click;
                        BottomCommandBar.PrimaryCommands.Add(CutButton);

                        AppBarButton DeleteButton = new AppBarButton
                        {
                            Icon = new SymbolIcon(Symbol.Delete),
                            Label = Globalization.GetString("Operate_Text_Delete")
                        };
                        DeleteButton.Click += Instance.Delete_Click;
                        BottomCommandBar.PrimaryCommands.Add(DeleteButton);

                        AppBarButton RenameButton = new AppBarButton
                        {
                            Icon = new SymbolIcon(Symbol.Rename),
                            Label = Globalization.GetString("Operate_Text_Rename")
                        };
                        RenameButton.Click += Instance.Rename_Click;
                        BottomCommandBar.PrimaryCommands.Add(RenameButton);

                        if (Item.StorageType == StorageItemTypes.File)
                        {
                            AppBarButton OpenButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.OpenFile),
                                Label = Globalization.GetString("Operate_Text_Open")
                            };
                            OpenButton.Click += Instance.FileOpen_Click;
                            BottomCommandBar.SecondaryCommands.Add(OpenButton);

                            MenuFlyout OpenFlyout = new MenuFlyout();
                            MenuFlyoutItem AdminItem = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uEA0D" },
                                Text = Globalization.GetString("Operate_Text_OpenAsAdministrator"),
                                IsEnabled = Instance.RunWithSystemAuthority.IsEnabled
                            };
                            AdminItem.Click += Instance.RunWithSystemAuthority_Click;
                            OpenFlyout.Items.Add(AdminItem);

                            MenuFlyoutItem OtherItem = new MenuFlyoutItem
                            {
                                Icon = new SymbolIcon(Symbol.SwitchApps),
                                Text = Globalization.GetString("Operate_Text_ChooseAnotherApp"),
                                IsEnabled = Instance.ChooseOtherApp.IsEnabled
                            };
                            OtherItem.Click += Instance.ChooseOtherApp_Click;
                            OpenFlyout.Items.Add(OtherItem);

                            BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.OpenWith),
                                Label = Globalization.GetString("Operate_Text_OpenWith"),
                                Flyout = OpenFlyout
                            });

                            BottomCommandBar.SecondaryCommands.Add(new AppBarSeparator());

                            MenuFlyout ToolFlyout = new MenuFlyout();
                            MenuFlyoutItem UnLock = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE785" },
                                Text = Globalization.GetString("Operate_Text_Unlock")
                            };
                            UnLock.Click += Instance.TryUnlock_Click;
                            ToolFlyout.Items.Add(UnLock);

                            MenuFlyoutItem Hash = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE2B2" },
                                Text = Globalization.GetString("Operate_Text_ComputeHash")
                            };
                            Hash.Click += Instance.CalculateHash_Click;
                            ToolFlyout.Items.Add(Hash);

                            BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                            {
                                Icon = new FontIcon { Glyph = "\uE90F" },
                                Label = Globalization.GetString("Operate_Text_Tool"),
                                Flyout = ToolFlyout
                            });

                            MenuFlyout EditFlyout = new MenuFlyout();
                            MenuFlyoutItem MontageItem = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE177" },
                                Text = Globalization.GetString("Operate_Text_Montage"),
                                IsEnabled = Instance.VideoEdit.IsEnabled
                            };
                            MontageItem.Click += Instance.VideoEdit_Click;
                            EditFlyout.Items.Add(MontageItem);

                            MenuFlyoutItem MergeItem = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE11E" },
                                Text = Globalization.GetString("Operate_Text_Merge"),
                                IsEnabled = Instance.VideoMerge.IsEnabled
                            };
                            MergeItem.Click += Instance.VideoMerge_Click;
                            EditFlyout.Items.Add(MergeItem);

                            MenuFlyoutItem TranscodeItem = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE1CA" },
                                Text = Globalization.GetString("Operate_Text_Transcode"),
                                IsEnabled = Instance.Transcode.IsEnabled
                            };
                            TranscodeItem.Click += Instance.Transcode_Click;
                            EditFlyout.Items.Add(TranscodeItem);

                            BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Edit),
                                Label = Globalization.GetString("Operate_Text_Edit"),
                                Flyout = EditFlyout
                            });

                            MenuFlyout ShareFlyout = new MenuFlyout();
                            MenuFlyoutItem SystemShareItem = new MenuFlyoutItem
                            {
                                Icon = new SymbolIcon(Symbol.Share),
                                Text = Globalization.GetString("Operate_Text_SystemShare")
                            };
                            SystemShareItem.Click += Instance.SystemShare_Click;
                            ShareFlyout.Items.Add(SystemShareItem);

                            MenuFlyoutItem WIFIShareItem = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE701" },
                                Text = Globalization.GetString("Operate_Text_WIFIShare")
                            };
                            WIFIShareItem.Click += Instance.WIFIShare_Click;
                            ShareFlyout.Items.Add(WIFIShareItem);

                            MenuFlyoutItem BluetoothShare = new MenuFlyoutItem
                            {
                                Icon = new FontIcon { Glyph = "\uE702" },
                                Text = Globalization.GetString("Operate_Text_BluetoothShare")
                            };
                            BluetoothShare.Click += Instance.BluetoothShare_Click;
                            ShareFlyout.Items.Add(BluetoothShare);

                            BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Share),
                                Label = Globalization.GetString("Operate_Text_Share"),
                                Flyout = ShareFlyout
                            });

                            AppBarButton CompressionButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Bookmarks),
                                Label = Instance.Zip.Label
                            };
                            CompressionButton.Click += Instance.Zip_Click;
                            BottomCommandBar.SecondaryCommands.Add(CompressionButton);

                            BottomCommandBar.SecondaryCommands.Add(new AppBarSeparator());

                            AppBarButton PropertyButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Tag),
                                Label = Globalization.GetString("Operate_Text_Property")
                            };
                            PropertyButton.Click += Instance.Attribute_Click;
                            BottomCommandBar.SecondaryCommands.Add(PropertyButton);
                        }
                        else
                        {
                            AppBarButton OpenButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.BackToWindow),
                                Label = Globalization.GetString("Operate_Text_Open")
                            };
                            OpenButton.Click += Instance.FolderOpen_Click;
                            BottomCommandBar.SecondaryCommands.Add(OpenButton);

                            AppBarButton NewWindowButton = new AppBarButton
                            {
                                Icon = new FontIcon { Glyph = "\uE727" },
                                Label = Globalization.GetString("Operate_Text_NewWindow")
                            };
                            NewWindowButton.Click += Instance.OpenFolderInNewWindow_Click;
                            BottomCommandBar.SecondaryCommands.Add(NewWindowButton);

                            AppBarButton NewTabButton = new AppBarButton
                            {
                                Icon = new FontIcon { Glyph = "\uF7ED" },
                                Label = Globalization.GetString("Operate_Text_NewTab")
                            };
                            NewTabButton.Click += Instance.OpenFolderInNewTab_Click;
                            BottomCommandBar.SecondaryCommands.Add(NewTabButton);

                            AppBarButton CompressionButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Bookmarks),
                                Label = Globalization.GetString("Operate_Text_Compression")
                            };
                            CompressionButton.Click += Instance.CompressFolder_Click;
                            BottomCommandBar.SecondaryCommands.Add(CompressionButton);

                            AppBarButton PinButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Add),
                                Label = Globalization.GetString("Operate_Text_PinToHome")
                            };
                            PinButton.Click += Instance.AddToLibray_Click;
                            BottomCommandBar.SecondaryCommands.Add(PinButton);

                            AppBarButton PropertyButton = new AppBarButton
                            {
                                Icon = new SymbolIcon(Symbol.Tag),
                                Label = Globalization.GetString("Operate_Text_Property")
                            };
                            PropertyButton.Click += Instance.FolderProperty_Click;
                            BottomCommandBar.SecondaryCommands.Add(PropertyButton);
                        }
                    }
                }
                else
                {
                    AppBarButton PasteButton = new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Paste),
                        Label = Globalization.GetString("Operate_Text_Paste")
                    };
                    PasteButton.Click += Instance.Paste_Click;
                    BottomCommandBar.PrimaryCommands.Add(PasteButton);

                    AppBarButton UndoButton = new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Undo),
                        Label = Globalization.GetString("Operate_Text_Undo")
                    };
                    UndoButton.Click += Instance.Undo_Click;
                    BottomCommandBar.PrimaryCommands.Add(UndoButton);

                    AppBarButton RefreshButton = new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Refresh),
                        Label = Globalization.GetString("Operate_Text_Refresh")
                    };
                    RefreshButton.Click += Instance.Refresh_Click;
                    BottomCommandBar.PrimaryCommands.Add(RefreshButton);

                    MenuFlyout NewFlyout = new MenuFlyout();
                    MenuFlyoutItem CreateFileItem = new MenuFlyoutItem
                    {
                        Icon = new SymbolIcon(Symbol.Page2),
                        Text = Globalization.GetString("Operate_Text_CreateFile"),
                        MinWidth = 150
                    };
                    CreateFileItem.Click += Instance.CreateFile_Click;
                    NewFlyout.Items.Add(CreateFileItem);

                    MenuFlyoutItem CreateFolder = new MenuFlyoutItem
                    {
                        Icon = new SymbolIcon(Symbol.NewFolder),
                        Text = Globalization.GetString("Operate_Text_CreateFolder"),
                        MinWidth = 150
                    };
                    CreateFolder.Click += Instance.CreateFolder_Click;
                    NewFlyout.Items.Add(CreateFolder);

                    BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Add),
                        Label = Globalization.GetString("Operate_Text_Create"),
                        Flyout = NewFlyout
                    });

                    bool DescCheck = false;
                    bool AscCheck = false;
                    bool NameCheck = false;
                    bool TimeCheck = false;
                    bool TypeCheck = false;
                    bool SizeCheck = false;

                    if (SortCollectionGenerator.Current.SortDirection == SortDirection.Ascending)
                    {
                        DescCheck = false;
                        AscCheck = true;
                    }
                    else
                    {
                        AscCheck = false;
                        DescCheck = true;
                    }

                    switch (SortCollectionGenerator.Current.SortTarget)
                    {
                        case SortTarget.Name:
                            {
                                TypeCheck = false;
                                TimeCheck = false;
                                SizeCheck = false;
                                NameCheck = true;
                                break;
                            }
                        case SortTarget.Type:
                            {
                                TimeCheck = false;
                                SizeCheck = false;
                                NameCheck = false;
                                TypeCheck = true;
                                break;
                            }
                        case SortTarget.ModifiedTime:
                            {
                                SizeCheck = false;
                                NameCheck = false;
                                TypeCheck = false;
                                TimeCheck = true;
                                break;
                            }
                        case SortTarget.Size:
                            {
                                NameCheck = false;
                                TypeCheck = false;
                                TimeCheck = false;
                                SizeCheck = true;
                                break;
                            }
                    }

                    MenuFlyout SortFlyout = new MenuFlyout();

                    RadioMenuFlyoutItem SortName = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortTarget_Name"),
                        IsChecked = NameCheck
                    };
                    SortName.Click += Instance.OrderByName_Click;
                    SortFlyout.Items.Add(SortName);

                    RadioMenuFlyoutItem SortTime = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortTarget_Time"),
                        IsChecked = TimeCheck
                    };
                    SortTime.Click += Instance.OrderByTime_Click;
                    SortFlyout.Items.Add(SortTime);

                    RadioMenuFlyoutItem SortType = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortTarget_Type"),
                        IsChecked = TypeCheck
                    };
                    SortType.Click += Instance.OrderByType_Click;
                    SortFlyout.Items.Add(SortType);

                    RadioMenuFlyoutItem SortSize = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortTarget_Size"),
                        IsChecked = SizeCheck
                    };
                    SortSize.Click += Instance.OrderBySize_Click;
                    SortFlyout.Items.Add(SortSize);

                    SortFlyout.Items.Add(new MenuFlyoutSeparator());

                    RadioMenuFlyoutItem Asc = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortDirection_Asc"),
                        IsChecked = AscCheck
                    };
                    Asc.Click += Instance.Asc_Click;
                    SortFlyout.Items.Add(Asc);

                    RadioMenuFlyoutItem Desc = new RadioMenuFlyoutItem
                    {
                        Text = Globalization.GetString("Operate_Text_SortDirection_Desc"),
                        IsChecked = DescCheck
                    };
                    Desc.Click += Instance.Desc_Click;
                    SortFlyout.Items.Add(Desc);

                    BottomCommandBar.SecondaryCommands.Add(new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Sort),
                        Label = Globalization.GetString("Operate_Text_Sort"),
                        Flyout = SortFlyout
                    });

                    BottomCommandBar.SecondaryCommands.Add(new AppBarSeparator());

                    AppBarButton PropertyButton = new AppBarButton
                    {
                        Icon = new SymbolIcon(Symbol.Tag),
                        Label = Globalization.GetString("Operate_Text_Property")
                    };
                    PropertyButton.Click += Instance.ParentProperty_Click;
                    BottomCommandBar.SecondaryCommands.Add(PropertyButton);

                    AppBarButton WinExButton = new AppBarButton
                    {
                        Icon = new FontIcon { Glyph = "\uEC50" },
                        Label = Globalization.GetString("Operate_Text_OpenInWinExplorer")
                    };
                    WinExButton.Click += Instance.UseSystemFileMananger_Click;
                    BottomCommandBar.SecondaryCommands.Add(WinExButton);

                    AppBarButton TerminalButton = new AppBarButton
                    {
                        Icon = new FontIcon { Glyph = "\uE756" },
                        Label = Globalization.GetString("Operate_Text_OpenInTerminal")
                    };
                    TerminalButton.Click += Instance.OpenInTerminal_Click;
                    BottomCommandBar.SecondaryCommands.Add(TerminalButton);
                }
            }
        }

        public void Dispose()
        {
            AddItemCancellation?.Cancel();
            AddItemCancellation?.Dispose();

            EnterLock.Dispose();
            TabViewContainer.ThisPage.FFInstanceContainer[this].AreaWatcher.Dispose();
        }

        private void Nav_PointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            Frame Frame = sender as Frame;

            int Delta = e.GetCurrentPoint(Frame).Properties.MouseWheelDelta;

            if (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            {
                if (Delta > 0)
                {
                    if (ItemDisplayMode.SelectedIndex > 0)
                    {
                        ItemDisplayMode.SelectedIndex -= 1;
                    }
                }
                else
                {
                    if (ItemDisplayMode.SelectedIndex < ItemDisplayMode.Items.Count - 1)
                    {
                        ItemDisplayMode.SelectedIndex += 1;
                    }
                }

                e.Handled = true;
            }
        }
    }
}
