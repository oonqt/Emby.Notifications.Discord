# Emby notifications for Discord

### Configuration

---

#### Discord Webhook URL (REQUIRED)
This is the incoming messages URL that the plugin uses to send notifications to. You can set this up via the channel or server settings in a Discord server. 

![creating webhooks](https://niger.gq/u/avt.gif)

#### Avatar URL (OPTIONAL)
This overrides the avatar of the webhook. This can be a link to anything as long as it returns *just* an image. This overrides the image set in the Discord webhook settings, if you wish to use the avatar you set in the Discord configuration, leave this blank.

![avatar url](https://niger.gq/u/3f4.png)

#### Webhook Username (OPTIONAL)
This overrides the username of the webhook. If you wish to set your username via Discord, you can set it in the webhook settings. The default webhook username provided via Discord is "Spidey bot".

![username](https://niger.gq/u/d84.png)

#### Mention Everyone
This gives you the ability to choose between 2 mention types, `@here`, `@everyone`, or `None`
The fundamental difference between `@everyone` and `@here` is `@everyone` will send the mention to everyone regardless of their status, while `@here` will only send the mention to the people who are currently online.


![mention example](https://niger.gq/u/fl8.png)

#### Embed Color
This sets the accent color of the Rich Embed. It can be set via a color picker or by entering a hex value. The default color is green.

![embed accent color](https://niger.gq/u/akd.png)