define([
    "loading",
    "emby-input",
    "emby-button",
    "emby-checkbox",
    "emby-select"
], function (loading) {
    var pluginId = "05C9ED79-5645-4AA2-A161-D4F29E63535C";
    var defaultEmbedColor = "#53b64c";

    function loadUserConfig(page, userId) {
        loading.show();

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            var discordConfig = config.Options.filter(function (config) {
                return userId === config.MediaBrowserUserId;
            })[0] || {};

            page.querySelector('#chkEnableDiscord').checked = discordConfig.Enabled || false;
            page.querySelector("#chkOverrideServerName").checked = discordConfig.ServerNameOverride || false;
            page.getElementById("mentionType").value = discordConfig.MentionType || "None";
            page.getElementById("txtDiscordWebhookUri").value = discordConfig.DiscordWebhookURI || "";
            page.getElementById("txtUsername").value = discordConfig.Username || "";
            page.getElementById("txtAvatarUrl").value = discordConfig.AvatarUrl || "";
            page.getElementById("embedColor").value = discordConfig.EmbedColor || defaultEmbedColor";
            page.getElementById("txtEmbedColor").value = discordConfig.MentionType || defaultEmbedColor";

            loading.hide();
        });
    }
});