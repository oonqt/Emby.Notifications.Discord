# Emby notifications for Discord

### [Forum Thread](https://emby.media/community/index.php?/topic/82370-new-plugin-discord-notifications/)

### Configuration

---

#### Discord Webhook URL (REQUIRED)
This is the incoming messages URL that the plugin uses to send notifications to. You can set this up via the channel or server settings in a Discord server. 

![creating webhooks](https://i.memester.xyz/u/avt.gif)

#### Avatar URL (OPTIONAL)
This overrides the avatar of the webhook. This can be a link to anything as long as it returns *just* an image. This overrides the image set in the Discord webhook settings. If you wish to use the avatar you set in the Discord configuration, leave this blank.

![avatar url](https://i.memester.xyz/u/3f4.png)

#### Webhook Username (OPTIONAL)
This overrides the username of the webhook. If you wish to set your username via Discord, you can set it in the webhook settings. 

![username](https://i.memester.xyz/u/d84.png)

#### Server name override
Checking this option gives you the ability to replace any occurance of "Emby Server" in your notifications with the name of your server (Applies to notification text as well as the notification footer).
##### I recommend you only disable this if it causes an issue

![Servernameoverride](https://i.memester.xyz/u/7n1.png)

#### Media Added Override
This replaces the default media override notification which is limited to displaying the file name with an Embed containing rich information (cover art, clickable links, etc)

FAQ: Why doesn't my cover art always show?
* Cover art will only load under two circumstances:
 1) The cover art was fetched from a remote source
 2) The cover art is Embedded in the file *AND* remote access is enabled 

##### I recommend you only disable this if it causes an issue

![media added override](https://i.memester.xyz/u/6n3.png)

#### Hide External Server Links
This will exclude any external links (i.e. title, local images) that have a URL tied to your EmbyServer. Only Enable this if you want to prevent people from seeing your Server URL. If you already have remote access disabled, there is no need to enable this as the links will already be excluded.

#### Mention Type
This gives you the ability to choose between 2 mention types, `@here`, `@everyone`, or `None`
The fundamental difference between `@everyone` and `@here` is `@everyone` will send the mention to everyone regardless of their status, while `@here` will only send the mention to the people who are currently online.

![mention example](https://i.memester.xyz/u/fl8.png)


#### Embed Color
This sets the accent color of the Rich Embed. It can be set via a color picker or by entering a hex value. The default color is green.

![embed accent color](https://i.memester.xyz/u/akd.png)
