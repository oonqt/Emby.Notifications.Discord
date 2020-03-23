using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Emby.Notifications.Discord.Configuration;
using MediaBrowser.Model.Drawing;
using System.IO;

namespace Emby.Notifications.Discord
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name
        {
            get { return "DiscordNotifications"; }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
                },
                new PluginPageInfo
                {
                    Name = $"{Name}JS",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js"
                }
            };
        }

        public override string Description
        {
            get
            {
                return "Sends notifications to Discord via webhooks.";
            }
        }

        private Guid _id = new Guid("05C9ED79-5645-4AA2-A161-D4F29E63535C");
        public override Guid Id
        {
            get { return _id; }
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.jpg");
        }

        public ImageFormat ThumbImageFormat
        {
            get
            {
                return ImageFormat.Jpg;
            }
        }

        public static Plugin Instance { get; private set; }
    }
}