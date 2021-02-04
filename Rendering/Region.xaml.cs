﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using XiboClient.Logic;
using XiboClient.Stats;

namespace XiboClient.Rendering
{
    /// <summary>
    /// Interaction logic for Region.xaml
    /// </summary>
    public partial class Region : UserControl
    {
        /// <summary>
        /// The RegionId
        /// </summary>
        public string Id { get;  private set; }

        /// <summary>
        /// Has the Layout Expired?
        /// </summary>
        public bool IsLayoutExpired = false;

        /// <summary>
        /// Has this Region Expired?
        /// </summary>
        public bool IsExpired = false;

        /// <summary>
        /// Is this region paused?
        /// </summary>
        private bool _isPaused = false;

        /// <summary>
        /// Is Pause Pending?
        /// </summary>
        private bool IsPausePending = false;

        /// <summary>
        /// This Regions zIndex
        /// </summary>
        public int ZIndex { get; set; }

        /// <summary>
        /// The list of media for this Region
        /// </summary>
        private XmlNodeList _media;

        /// <summary>
        /// The Region Options
        /// </summary>
        private RegionOptions options;

        /// <summary>
        /// Current Media
        /// </summary>
        private Media currentMedia;

        /// <summary>
        /// The current media options
        /// </summary>
        private MediaOptions _currentMediaOptions;

        /// <summary>
        /// Media we have navigated to, interrupting the usual flow
        /// </summary>
        private Media navigatedMedia;

        /// <summary>
        /// Track the current sequence
        /// </summary>
        private int currentSequence = -1;
        private bool _sizeResetRequired;
        private bool _dimensionsSet = false;
        private int _audioSequence;
        private double _currentPlaytime;

        /// <summary>
        /// Event to indicate that this Region's duration has elapsed
        /// </summary>
        public delegate void DurationElapsedDelegate();
        public event DurationElapsedDelegate DurationElapsedEvent;

        /// <summary>
        /// Event to indicate that some media has expired.
        /// </summary>
        public delegate void MediaExpiredDelegate();
        public event MediaExpiredDelegate MediaExpiredEvent;

        /// <summary>
        /// Widget Date WaterMark
        /// </summary>
        private int _widgetAvailableTtl;

        public Region()
        {
            InitializeComponent();
            ZIndex = 0;
        }

        public void LoadFromOptions(string id, RegionOptions options, XmlNodeList media)
        {
            // Start of by setting our dimensions
            SetDimensions(options.left, options.top, options.width, options.height);

            // Store the options
            Id = id;
            this.options = options;
            _media = media;
        }

        /// <summary>
        /// Get the Rect representing this regions dimensions
        /// </summary>
        /// <returns></returns>
        public Rect GetRect()
        {
            return new Rect
            {
                Width = this.Width,
                Height = this.Height,
                X = this.options.top,
                Y = this.options.left
            };
        }

        /// <summary>
        /// Get Current WidgetId
        /// </summary>
        /// <returns></returns>
        public string GetCurrentInteractiveWidgetId()
        {
            return this.navigatedMedia?.Id;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            // Start this region
            this.currentSequence = -1;
            StartNext(0);
        }

        /// <summary>
        /// Move to the Next item
        /// </summary>
        public void Previous()
        {
            // Drop back a count of 2 (we move on in StartNext);
            this.currentSequence -= 2;

            // If we're less than 0, move back one from the end
            if (this.currentSequence < 0)
            {
                this.currentSequence = this._media.Count - 2;
            }

            Next();
        }

        /// <summary>
        /// Move to the Next item
        /// </summary>
        public void Next()
        {
            // Shimmy onto the STA thread
            Dispatcher.Invoke(new System.Action(() => {
                // Call start next
                StartNext(0);
            }));
        }

        /// <summary>
        /// Navigate to the provided XmlNode
        /// </summary>
        /// <param name="media">A Media XmlNode</param>
        public void NavigateToWidget(XmlNode node)
        {
            // Create the options and media node
            Media media = CreateNextMediaNode(Media.ParseOptions(node));

            // Where are we?
            _currentPlaytime = this.currentMedia.CurrentPlaytime();

            // UI thread
            Dispatcher.Invoke(new System.Action(() => {
                try
                {
                    // Stop Audio
                    StopAudio();

                    // Start our new one.
                    StartMedia(media, 0);

                    // Do we have a navigate to media already active?
                    if (navigatedMedia != null)
                    {
                        StopMedia(navigatedMedia);
                    }
                    else
                    {
                        // Stop Normal Media
                        StopMedia(currentMedia);
                    }

                    // Switch-a-roo
                    navigatedMedia = media;
                } 
                catch (Exception e)
                {
                    Trace.WriteLine(new LogMessage("Region", "NavigateToWidget: e = " + e.Message), LogType.Error.ToString());
                }
            }));
        }

        /// <summary>
        /// Extend the current widgets duration by the provided amount
        /// </summary>
        /// <param name="duration"></param>
        public void ExtendCurrentWidgetDuration(int duration)
        {
            if (navigatedMedia != null)
            {
                navigatedMedia.ExtendDuration(duration);
            }
            else
            {
                currentMedia.ExtendDuration(duration);
            }
        }

        /// <summary>
        /// Set the current widgets duration to the provided amount
        /// </summary>
        /// <param name="duration"></param>
        public void SetCurrentWidgetDuration(int duration)
        {
            if (navigatedMedia != null)
            {
                navigatedMedia.SetDuration(duration);
            }
            else
            {
                currentMedia.SetDuration(duration);
            }
        }

        /// <summary>
        /// Start the Next Media
        /// <paramref name="position"/>
        /// </summary>
        private void StartNext(double position)
        {
            Debug.WriteLine("StartNext: Region " + this.options.regionId + " starting next sequence " + this.currentSequence + " at position " + position, "Region");

            if (!this._dimensionsSet)
            {
                // Evaluate the width, etc
                SetDimensions(this.options.left, this.options.top, this.options.width, this.options.height);

                // We've set the dimensions
                this._dimensionsSet = true;
            }

            // Try to populate a new media object for this region
            Media newMedia;

            // Loop around trying to start the next media
            bool startSuccessful = false;
            int countTries = 0;

            while (!startSuccessful)
            {
                // If we go round this the same number of times as media objects, then we are unsuccessful and should exception
                if (countTries >= this._media.Count)
                    throw new ArgumentOutOfRangeException("Unable to set and start a media node");

                // Lets try again
                countTries++;

                // Only use the position if this is the first Widget we've tried to start.
                // otherwise start from the beginning
                if (countTries > 1)
                {
                    position = 0;
                }

                // Store the current sequence
                int temp = this.currentSequence;

                // Before we can try to set the next media node, we need to stop any currently running Audio
                StopAudio();

                // Set the next media node for this panel
                if (!SetNextMediaNodeInOptions())
                {
                    // For some reason we cannot set a media node... so we need this region to become invalid
                    CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Region, options.layoutId, options.regionId, "Unable to set any region media nodes.", _widgetAvailableTtl);

                    // Throw this out so we remove the Layout
                    throw new InvalidOperationException("Unable to set any region media nodes.");
                }

                // If the sequence hasnt been changed, OR the layout has been expired
                // there has been no change to the sequence, therefore the media we have already created is still valid
                // or this media has actually been destroyed and we are working out way out the call stack
                if (IsLayoutExpired)
                {
                    return;
                }
                else if (this.currentSequence == temp)
                {
                    // Media has not changed, we are likely the only valid media item in the region
                    // the layout has not yet expired, so depending on whether we loop or not, we either
                    // reload the same media item again
                    // or do nothing (return)
                    // This could be made more succinct, but is clearer written as an elseif.
                    if (!this.options.RegionLoop)
                    {
                        return;
                    }
                }

                // See if we can start the new media object
                try
                {
                    newMedia = CreateNextMediaNode(_currentMediaOptions);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region", "StartNext: Unable to create new " + _currentMediaOptions.type 
                        + "  object: " + ex.Message), LogType.Error.ToString());

                    // Try the next node
                    startSuccessful = false;
                    continue;
                }

                // New Media record has been created
                // ------------
                // Start the new media
                try
                {
                    // See if we need to change our Region Dimensions
                    if (newMedia.RegionSizeChangeRequired())
                    {
                        SetDimensions(0, 0, this.options.PlayerWidth, this.options.PlayerHeight);

                        // Set size reset for the next time around.
                        _sizeResetRequired = true;
                    }
                    else if (_sizeResetRequired)
                    {
                        SetDimensions(this.options.left, this.options.top, this.options.width, this.options.height);
                        _sizeResetRequired = false;
                    }

                    Debug.WriteLine("StartNext: Calling start on media in regionId " + this.options.regionId + ", position " + position, "Region");

                    // Start.
                    StartMedia(newMedia, position);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region", "StartNext: Unable to start new " + _currentMediaOptions.type + "  object: " + ex.Message), LogType.Error.ToString());
                    startSuccessful = false;
                    continue;
                }

                startSuccessful = true;

                // Remove the old media
                if (navigatedMedia != null)
                {
                    Debug.WriteLine("StartNext: Stopping navigated media in regionId " + this.options.regionId + ", position " + position, "Region");

                    StopMedia(navigatedMedia);

                    navigatedMedia = null;
                }
                else if (currentMedia != null)
                {
                    Debug.WriteLine("StartNext: Stopping current media in regionId " + this.options.regionId + ", position " + position, "Region");

                    StopMedia(currentMedia);

                    currentMedia = null;
                }

                // Change the reference 
                currentMedia = newMedia;

                // Open a stat record
                // options accurately reflect the current media, so we can use them.
                StatManager.Instance.WidgetStart(this.options.scheduleId, this.options.layoutId, _currentMediaOptions.mediaid);
            }
        }

        /// <summary>
        /// Sets the next media node. Should be used either from a mediaComplete event, or an options reset from 
        /// the parent.
        /// </summary>
        private bool SetNextMediaNodeInOptions()
        {
            // What if there are no media nodes?
            if (_media.Count == 0)
            {
                Trace.WriteLine(new LogMessage("Region", "SetNextMediaNode: No media nodes to display"), LogType.Audit.ToString());

                return false;
            }

            // Tidy up old audio if necessary
            foreach (Media audio in _currentMediaOptions.Audio)
            {
                try
                {
                    // Unbind any events and dispose
                    audio.DurationElapsedEvent -= Audio_DurationElapsedEvent;
                    audio.Stop(false);
                }
                catch
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNodeInOptions: Unable to dispose of audio item"), LogType.Audit.ToString());
                }
            }

            // Empty the options node
            _currentMediaOptions.Audio.Clear();

            // Get a media node
            bool validNode = false;
            int numAttempts = 0;

            // Loop through all the nodes in order
            while (numAttempts < this._media.Count)
            {
                // Move the sequence on
                this.currentSequence++;

                if (this.currentSequence >= _media.Count)
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNodeInOptions: Region " + this.options.regionId + " Expired"), LogType.Audit.ToString());

                    // Start from the beginning
                    this.currentSequence = 0;

                    // We have expired (want to raise an expired event to the parent)
                    IsExpired = true;

                    // Region Expired
                    DurationElapsedEvent?.Invoke();

                    // We want to continue on to show the next media (unless the duration elapsed event triggers a region change)
                    if (IsLayoutExpired)
                    {
                        return true;
                    }
                }

                // Get the media node for this sequence
                XmlNode mediaNode = _media[this.currentSequence];

                try
                {
                    // Get media options representing the incoming media node.
                    _currentMediaOptions = Media.ParseOptions(mediaNode);

                    // Decorate it with common properties.
                    _currentMediaOptions.DecorateWithRegionOptions(this.options);
                }
                catch
                {
                    // Increment the number of attempts and try again
                    numAttempts++;

                    // Carry on
                    continue;
                }

                // Is this widget inside the from/to date?
                if (!(_currentMediaOptions.FromDt <= DateTime.Now && _currentMediaOptions.ToDt > DateTime.Now))
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNode: Widget outside from/to date."), LogType.Audit.ToString());

                    // Increment the number of attempts and try again
                    numAttempts++;

                    // Watermark the next earliest time we can expect this Widget to be available.
                    if (_currentMediaOptions.FromDt > DateTime.Now)
                    {
                        if (_widgetAvailableTtl == 0)
                        {
                            _widgetAvailableTtl = (int)(_currentMediaOptions.FromDt - DateTime.Now).TotalSeconds;
                        }
                        else
                        {
                            _widgetAvailableTtl = Math.Min(_widgetAvailableTtl, (int)(_currentMediaOptions.FromDt - DateTime.Now).TotalSeconds);
                        }
                    }

                    // Carry on
                    continue;
                }

                // We have a valid node
                validNode = true;
                break;
            }

            // If we dont have a valid node out of all the nodes in the region, then return false.
            if (!validNode)
                return false;

            Trace.WriteLine(new LogMessage("Region", "SetNextMediaNode: New media detected " + _currentMediaOptions.type), LogType.Audit.ToString());

            return true;
        }

        /// <summary>
        /// Create the next media node based on the provided options
        /// </summary>
        /// <returns></returns>
        private Media CreateNextMediaNode(MediaOptions options)
        {
            Trace.WriteLine(new LogMessage("Region - CreateNextMediaNode", string.Format("Creating new media: {0}, {1}", options.type, options.mediaid)), LogType.Audit.ToString());

            Media media = Media.Create(options);

            // Set the media width/height
            media.Width = Width;
            media.Height = Height;

            // Sets up the timer for this media, if it hasn't already been set
            if (media.Duration == 0)
            {
                media.Duration = options.duration;
            }

            // Add event handler for when this completes
            media.DurationElapsedEvent += new Media.DurationElapsedDelegate(Media_DurationElapsedEvent);

            // Add event handlers for audio
            foreach (Media audio in options.Audio)
            {
                audio.DurationElapsedEvent += Audio_DurationElapsedEvent;
            }

            return media;
        }

        /// <summary>
        /// Start the provided media
        /// </summary>
        /// <param name="media"></param>
        private void StartMedia(Media media, double position)
        {
            Trace.WriteLine(new LogMessage("Region", "StartMedia: Starting media at position: " + position), LogType.Audit.ToString());

            // Add to this scene
            this.RegionScene.Children.Add(media);

            // Render the media, this adds the child controls to the Media UserControls grid
            media.RenderMedia(position);

            // Reset the audio sequence and start
            _audioSequence = 1;
            StartAudio();
        }

        /// <summary>
        /// Start Audio if necessary
        /// </summary>
        private void StartAudio()
        {
            // Start any associated audio
            if (_currentMediaOptions.Audio.Count >= _audioSequence)
            {
                Media audio = _currentMediaOptions.Audio[_audioSequence - 1];

                // call render media and add to controls
                audio.RenderMedia(0);

                // Add to this scene
                this.RegionScene.Children.Add(audio);
            }
        }

        /// <summary>
        /// Audio Finished Playing
        /// </summary>
        /// <param name="filesPlayed"></param>
        private void Audio_DurationElapsedEvent(int filesPlayed)
        {
            try
            {
                StopMedia(_currentMediaOptions.Audio[_audioSequence - 1]);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Region - audio_DurationElapsedEvent", "Audio -  Unable to dispose. Ex = " + ex.Message), LogType.Audit.ToString());
            }

            _audioSequence += filesPlayed;

            // Start
            StartAudio();
        }

        /// <summary>
        /// Stop Media
        /// </summary>
        /// <param name="media"></param>
        private void StopMedia(Media media)
        {
            StopMedia(media, false);
        }

        /// <summary>
        /// Stop normal media node
        /// </summary>
        /// <param name="media"></param>
        /// <param name="regionStopped"></param>
        private void StopMedia(Media media, bool regionStopped)
        {
            Trace.WriteLine(new LogMessage("Region", "StopMedia: " + media.Id + " stopping, region stopped " + regionStopped), LogType.Audit.ToString());

            // Dispose of the current media
            try
            {
                // Close the stat record
                StatManager.Instance.WidgetStop(media.ScheduleId, media.LayoutId, media.Id, media.StatsEnabled);

                // Media Stopped Event removes the media from the scene
                media.MediaStoppedEvent += Media_MediaStoppedEvent;

                // Tidy Up
                media.DurationElapsedEvent -= Media_DurationElapsedEvent;
                media.Stop(regionStopped);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Region", "StopMedia: Unable to stop. Ex = " + ex.Message), LogType.Audit.ToString());

                // Remove the controls
                RegionScene.Children.Remove(media);
            }
        }

        /// <summary>
        /// This media has stopped
        /// </summary>
        /// <param name="media"></param>
        private void Media_MediaStoppedEvent(Media media)
        {
            Trace.WriteLine(new LogMessage("Region", "Media_MediaStoppedEvent: " + media.Id), LogType.Audit.ToString());

            media.MediaStoppedEvent -= Media_MediaStoppedEvent;
            media.Stopped();

            // Remove the controls
            RegionScene.Children.Remove(media);
        }

        /// <summary>
        /// Stop Audio
        /// </summary>
        private void StopAudio()
        {
            // Stop the currently playing audio (if there is any)
            if (_currentMediaOptions.Audio.Count > 0)
            {
                try
                {
                    StopMedia(_currentMediaOptions.Audio[_audioSequence - 1]);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region - Stop Media", "Audio -  Unable to dispose. Ex = " + ex.Message), LogType.Audit.ToString());
                }
            }
        }

        /// <summary>
        /// The media has elapsed
        /// </summary>
        private void Media_DurationElapsedEvent(int filesPlayed)
        {
            Trace.WriteLine(new LogMessage("Region", string.Format("DurationElapsedEvent: Media Elapsed: {0}", _currentMediaOptions.uri)), LogType.Audit.ToString());

            if (navigatedMedia != null)
            {
                // Drop the currentSequence
                this.currentSequence--;
            } 
            else if (filesPlayed > 1)
            {
                // Increment the _current sequence by the number of filesPlayed (minus 1)
                this.currentSequence += (filesPlayed - 1);
            }

            // Indicate that this media has expired.
            MediaExpiredEvent?.Invoke();

            // If this layout has been expired we know that everything will soon be torn down, so do nothing
            if (IsLayoutExpired)
            {
                Debug.WriteLine("DurationElapsedEvent: Layout Expired, therefore we don't StartNext", "Region");
                return;
            }

            // If we are now paused, we don't start the next media
            if (this._isPaused)
            {
                Debug.WriteLine("DurationElapsedEvent: Paused, therefore we don't StartNext", "Region");
                return;
            }

            // If Pause Pending, then stop here as we will be removed
            if (IsPausePending)
            {
                Debug.WriteLine("DurationElapsedEvent: Pause Pending, therefore we don't StartNext", "Region");
                return;
            }

            // TODO:
            // Animate out at this point if we need to
            // the result of the animate out complete event should then move us on.
            // this.currentMedia.TransitionOut();

            // make some decisions about what to do next
            try
            {
                double startAt = navigatedMedia != null ? _currentPlaytime : 0;
                StartNext(startAt);
            }
            catch (Exception e)
            {
                Trace.WriteLine(new LogMessage("Region", "DurationElapsedEvent: E=" + e.Message), LogType.Error.ToString());

                // What do we do if there is an exception moving to the next media node?
                // For some reason we cannot set a media node... so we need this region to become invalid
                IsExpired = true;

                // Fire elapsed
                DurationElapsedEvent?.Invoke();

                return;
            }
        }

        /// <summary>
        /// Set Dimensions
        /// </summary>
        /// <param name="left"></param>
        /// <param name="top"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void SetDimensions(int left, int top, int width, int height)
        {
            Debug.WriteLine("Setting Dimensions to W:" + width + ", H:" + height + ", (" + left + "," + top + ")");

            // Evaluate the width, etc
            Width = width;
            Height = height;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(left, top, 0, 0);
        }

        /// <summary>
        /// Set Dimensions
        /// </summary>
        /// <param name="location"></param>
        /// <param name="size"></param>
        private void SetDimensions(Point location, Size size)
        {
            SetDimensions((int)location.X, (int)location.Y, (int)size.Width, (int)size.Height);
        }

        /// <summary>
        /// Is Pause Pending?
        /// </summary>
        public void PausePending()
        {
            this.IsPausePending = true;
        }

        /// <summary>
        /// Pause this Layout
        /// </summary>
        public void Pause()
        {
            if (this.currentMedia != null)
            {
                // Store the current playtime of this Region.
                this._currentPlaytime = this.currentMedia.CurrentPlaytime();

                // Stop and remove the current media.
                StopMedia(this.currentMedia, true);

                // Remove it.
                this.currentMedia = null;

                Debug.WriteLine("Pause: paused Region, current Playtime is " + this._currentPlaytime, "Region");
            }

            // Paused
            this._isPaused = true;
            this.IsPausePending = false;
        }

        /// <summary>
        /// Resume this Layout
        /// </summary>
        public void Resume(bool isInterrupt)
        {
            // If we are an interrupt, we should skip on to the next item
            // and if there is only 1 item, we should replay it.
            // if we are a normal layout, then we resume the current one.
            if (isInterrupt)
            {
                if (this._media.Count <= 1)
                {
                    this.currentSequence--;
                }

                // Start media item
                StartNext(0);
            }
            else
            {
                // We have to dial back the current position here, because start next will straight away increment it
                this.currentSequence--;

                // Resume the current media item
                StartNext(this._currentPlaytime);
            }

            this._isPaused = false;
        }

        /// <summary>
        /// Clears the Region of anything that it shouldnt still have... 
        /// called when Destroying a Layout and when Removing an Overlay
        /// </summary>
        public void Clear()
        {
            try
            {
                // Stop Audio
                StopAudio();

                // Stop the current media item
                if (this.currentMedia != null)
                {
                    StopMedia(this.currentMedia);
                }
            }
            catch
            {
                Trace.WriteLine(new LogMessage("Region - Clear", "Error closing off stat record"), LogType.Error.ToString());
            }
        }
    }
}
