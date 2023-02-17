﻿using Carpeddit.Api.Enums;
using Carpeddit.Api.Models;
using Carpeddit.Api.Services;
using Carpeddit.App.Models;
using Carpeddit.App.ViewModels;
using Carpeddit.Common.Collections;
using Carpeddit.Common.Helpers;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.UI;
using Microsoft.Toolkit.Uwp.UI.Animations.Expressions;
using System;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Carpeddit.App.Views
{
    public sealed partial class SubredditInfoPage : Page
    {
        private Subreddit Subreddit;

        private BulkObservableCollection<PostViewModel> _posts = new();
        private IRedditService service = App.Services.GetService<IRedditService>();

        private bool isLoadingMore;

        private CompositionPropertySet? _scrollerPropertySet;
        private Compositor? _compositor;
        private SpriteVisual? _backgroundVisual;
        private ScrollViewer? _scrollViewer;

        public SubredditInfoPage()
        {
            InitializeComponent();
        }

        private async void SubredditInfoPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadingInfoRing.IsActive = false;
            LoadingInfoRing.Visibility = Visibility.Collapsed;

            _ = VisualStateManager.GoToState(this, (Subreddit.UserIsSubscriber ?? false) ? "JoinedState" : "NotJoinedState", false);

            if (string.IsNullOrWhiteSpace(Subreddit.Title) || Subreddit.Title.Equals(Subreddit.DisplayNamePrefixed))
                _ = VisualStateManager.GoToState(this, "NoDisplayName", false);

            _scrollViewer = MainList.FindDescendant<ScrollViewer>();

            _scrollerPropertySet = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_scrollViewer);
            _compositor = _scrollerPropertySet.Compositor;

            ManipulationPropertySetReferenceNode scrollingProperties = _scrollerPropertySet.GetSpecializedReference<ManipulationPropertySetReferenceNode>();

            CreateImageBackgroundGradientVisual(scrollingProperties.Translation.Y, string.IsNullOrEmpty(Subreddit.BannerBackgroundImage) ? null : new(WebUtility.HtmlDecode(Subreddit.BannerBackgroundImage)));

            PostLoadingProgressRing.IsActive = true;
            PostLoadingProgressRing.Visibility = Visibility.Visible;

            var posts = (await service.GetSubredditPostsAsync(Subreddit.DisplayName, SortMode.Hot, new(limit: 50))).Select(p => new PostViewModel()
            {
                Post = p
            });

            _posts.AddRange(posts);

            MainList.ItemsSource = _posts;

            PostLoadingProgressRing.IsActive = false;
            PostLoadingProgressRing.Visibility = Visibility.Collapsed;

            var scrollViewer = ListHelpers.GetScrollViewer(MainList);

            scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
        }

        private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;

            if (isLoadingMore || (scrollViewer.VerticalOffset > scrollViewer.ScrollableHeight - 36 && e.IsIntermediate))
                return;

            isLoadingMore = true;

            FooterProgress.Visibility = Visibility.Visible;

            var posts = (await service.GetSubredditPostsAsync(Subreddit.DisplayName, SortMode.Hot, new(after: _posts.Last().Post.Name, limit: 50))).Select(p => new PostViewModel()
            {
                Post = p
            });

            _posts.AddRange(posts);

            FooterProgress.Visibility = Visibility.Collapsed;

            isLoadingMore = false;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Subreddit subreddit)
            {
                Subreddit = subreddit;
                Loaded += SubredditInfoPage_Loaded;
            }
            else if (e.Parameter is string name)
            {
                Subreddit = await service.GetSubredditInfoAsync(name);
                SubredditInfoPage_Loaded(null, null);
                Bindings.Update();
            }
        }

        [RelayCommand]
        private void UserClick(string name)
        {
            if (name == "[deleted]")
                return;

            Frame.Navigate(typeof(ProfilePage), name);
        }

        [RelayCommand]
        private void TitleClick(PostViewModel model)
            => ((Frame)Window.Current.Content).Navigate(typeof(PostDetailsPage), new PostDetailsNavigationInfo()
            {
                ShowFullPage = true,
                ItemData = model
            });

        [RelayCommand]
        private async Task JoinOrLeaveSubredditAsync()
        {
            try
            {
                var subreddits = new[] { Subreddit.Name };

                if (Subreddit.UserIsSubscriber ?? false)
                {
                    await service.UnsubscribeFromSubredditsAsync(subreddits);
                    _ = VisualStateManager.GoToState(this, "NotJoinedState", false);
                    Subreddit.UserIsSubscriber = false;
                    return;
                }

                await service.SubscribeToSubredditsAsync(subreddits);
                _ = VisualStateManager.GoToState(this, "JoinedState", false);
                Subreddit.UserIsSubscriber = true;
            }
            catch (Exception)
            {

            }
        }

        private void OnCopyLinkFlyoutItemClick(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)e.OriginalSource).DataContext is not PostViewModel item)
                return;

            var package = new DataPackage()
            {
                RequestedOperation = DataPackageOperation.Copy,
            };

            package.SetText("https://www.reddit.com" + item.Post.Permalink);

            Clipboard.SetContent(package);
        }

        private void BackgroundHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_backgroundVisual == null) return;
            _backgroundVisual.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        }

        private void CreateImageBackgroundGradientVisual(ScalarNode scrollVerticalOffset, Uri uri)
        {
            if (string.IsNullOrEmpty(uri?.AbsoluteUri) || _compositor == null) return;

            var imageSurface = LoadedImageSurface.StartLoadFromUri(uri);
            imageSurface.LoadCompleted += OnImageSurfaceLoadCompleted;
            var imageBrush = _compositor.CreateSurfaceBrush(imageSurface);
            imageBrush.HorizontalAlignmentRatio = 0.5f;
            imageBrush.VerticalAlignmentRatio = 0.5f;
            imageBrush.Stretch = CompositionStretch.UniformToFill;

            var gradientBrush = _compositor.CreateLinearGradientBrush();
            gradientBrush.EndPoint = new Vector2(0, 1);
            gradientBrush.MappingMode = CompositionMappingMode.Relative;
            gradientBrush.ColorStops.Add(_compositor.CreateColorGradientStop(0.6f, Colors.White));
            gradientBrush.ColorStops.Add(_compositor.CreateColorGradientStop(1, Colors.Transparent));

            var maskBrush = _compositor.CreateMaskBrush();
            maskBrush.Source = imageBrush;
            maskBrush.Mask = gradientBrush;

            var visual = _backgroundVisual = _compositor.CreateSpriteVisual();
            visual.Size = new Vector2((float)BackgroundHost.ActualWidth, (float)BackgroundHost.Height);
            visual.Brush = maskBrush;

            gradientBrush.StartAnimation("Offset.Y", scrollVerticalOffset * 0.15f);

            ElementCompositionPreview.SetElementChildVisual(BackgroundHost, visual);
        }

        private void OnImageSurfaceLoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
        {
            sender.LoadCompleted -= OnImageSurfaceLoadCompleted;

            var animation = _compositor.CreateScalarKeyFrameAnimation();

            animation.InsertKeyFrame(0, 0);
            animation.InsertKeyFrame(1, 1, _compositor.CreateLinearEasingFunction());

            animation.Duration = TimeSpan.FromMilliseconds(600);
            _backgroundVisual.StartAnimation("Opacity", animation);
        }
    }
}
