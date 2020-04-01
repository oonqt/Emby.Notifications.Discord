define([
    "loading",
    "alert",
    "dialogHelper",
    "emby-input",
    "emby-button",
    "formDialogStyle",
    "flexStyles",
    "emby-linkbutton",
    "emby-checkbox",
    "emby-select"
], function (loading, alert, dialogHelper) {
        // ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!ebr let me in the plugin catalog!

        var pluginId = "05C9ED79-5645-4AA2-A161-D4F29E63535C";
        var defaultEmbedColor = "#53b64c";

        function loadUserConfig(page, userId) {
            loading.show();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                var discordConfig = config.Options.filter(function (config) {
                    return userId === config.MediaBrowserUserId;
                })[0] || {};

                page.querySelector('#chkEnableDiscord').checked = discordConfig.Enabled || false;
                page.querySelector("#chkExcludeExternalServerLinks").checked = discordConfig.ExcludeExternalServerLinks || false;
                page.querySelector("#chkOverrideServerName").checked = isEmpty(discordConfig.ServerNameOverride) ? true : discordConfig.ServerNameOverride;
                page.querySelector("#chkOverrideMediaAdded").checked = isEmpty(discordConfig.MediaAddedOverride) ? true : discordConfig.MediaAddedOverride;
                page.querySelector("#mentionType").value = discordConfig.MentionType || "None";
                page.querySelector("#txtDiscordWebhookUri").value = discordConfig.DiscordWebhookURI || "";
                page.querySelector("#txtUsername").value = discordConfig.Username || "";
                page.querySelector("#txtAvatarUrl").value = discordConfig.AvatarUrl || "";
                page.querySelector("#embedColor").value = discordConfig.EmbedColor || defaultEmbedColor;
                page.querySelector("#txtEmbedColor").value = discordConfig.EmbedColor || defaultEmbedColor;

                loading.hide();
            });
        }

        function isEmpty(value) {
            if (
                value === undefined ||
                value === null ||
                (typeof value === "object" && Object.keys(value).length === 0) ||
                (typeof value === "string" && value.trim() === 0)
            ) return true; 
        }

        function saveConfig(e) {
            e.preventDefault();

            var page = this;

            loading.show();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                var userId = page.querySelector("#selectUser").value;

                var discordConfig = config.Options.filter(function (config) {
                    return userId === config.MediaBrowserUserId;
                })[0];

                if (!discordConfig) {
                    discordConfig = {};
                    config.Options.push(discordConfig);
                } 

                discordConfig.MediaBrowserUserId = userId;
                discordConfig.ExcludeExternalServerLinks = page.querySelector("#chkExcludeExternalServerLinks").checked;
                discordConfig.ServerNameOverride = page.querySelector("#chkOverrideServerName").checked;
                discordConfig.MediaAddedOverride = page.querySelector("#chkOverrideMediaAdded").checked;
                discordConfig.Enabled = page.querySelector('#chkEnableDiscord').checked;
                discordConfig.MentionType = page.querySelector("#mentionType").value;
                discordConfig.DiscordWebhookURI = page.querySelector("#txtDiscordWebhookUri").value;
                discordConfig.Username = page.querySelector("#txtUsername").value;
                discordConfig.AvatarUrl = page.querySelector("#txtAvatarUrl").value;
                discordConfig.EmbedColor = page.querySelector("#embedColor").value;

                ApiClient.updatePluginConfiguration(pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
            });
        }

        function testNotification(page) {
            loading.show();

            var onError = function (data) {
                loading.hide();

                if (data.status === 400) {
                    alert("${ErrorWebhookInvalid}");
                    //The Discord Webhook URL appears to be invalid. Please try again with a valid URL
                } else if (data.status === 500) {
                    var dialogOptions = { removeOnClose: true, scrollY: !1, size: "small" }
                    var dialog = dialogHelper.createDialog(dialogOptions);
                    dialog.classList.add("formDialog"),
                        dialog.classList.add("justify-content-center"),
                        dialog.style.height = "150px",
                        dialog.classList.add("align-items-center"),
                        dialog.innerHTML = '<div class="formDialogHeader formDialogHeader-clear justify-content-center"><h2 class="formDialogHeaderTitle hide" style="margin-left:0;margin-top: .5em;padding: 0 1em;"></h2></div><div is="emby-scroller" data-horizontal="false" data-centerfocus="card" class="formDialogContent emby-scroller no-grow scrollY" style="width:100%;"><div class="scrollSlider dialogContentInner dialog-content-centered padded-left padded-right scrollSliderY" style="text-align:center;padding-bottom:1em;">${ErrorInternalServer}</div></div><div class="formDialogFooter formDialogFooter-clear formDialogFooter-flex"><button id="dialogSubmitBtn-3434321" is="emby-button" type="button" class="btnOption raised formDialogFooterItem formDialogFooterItem-autosize button-submit emby-button" data-id="ok" autofocus="">${LabelOkay}</button></div>';

                    dialogHelper.open(dialog);

                    // im too lazy to reverse engineer the dashboard code to find how to properly attach buttons, this works for now lol
                    document.getElementById("dialogSubmitBtn-3434321").addEventListener("click", function () {
                        dialogHelper.close(dialog);
                    });
                }
            }

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                var selectedUser = page.querySelector("#selectUser").value;

                var userConfig = config.Options.filter(function (config) {
                    return selectedUser === config.MediaBrowserUserId;
                })[0];

                if (!userConfig) {
                    loading.hide();
                    alert("${MessageConfigureNotifications}");
                }

                ApiClient.ajax({
                    type: "POST",
                    url: ApiClient.getUrl("Notification/Discord/Test/" + userConfig.MediaBrowserUserId)
                }).then(function () {
                    loading.hide();

                    alert("${MessageNotificationSent}");
                }, onError);
            });
        }

        function trigger(element, type) {
            if ('createEvent' in document) {
                // modern browsers, IE9+
                var e = document.createEvent('HTMLEvents');
                e.initEvent(type, false, true);
                element.dispatchEvent(e);
            } else {
                // IE 8
                var e = document.createEventObject();
                e.eventType = type;
                element.fireEvent('on' + e.eventType, e);
            }
        }

        function loadUsers(page) {
            loading.show();

            ApiClient.getUsers().then(function (users) {
                var selectUsers = page.querySelector("#selectUser");

                selectUsers.innerHTML = users.map(function (user) {
                    return '<option value="' + user.Id + '">' + user.Name + '</option>';
                });

                trigger(selectUsers, "change");
            });

            loading.hide();
        }

        return function (view) {
            view.querySelector("form").addEventListener("submit", saveConfig);

            view.addEventListener("viewshow", function () {
                var page = this;

                loadUsers(page); // load all users into select

                page.querySelector("#selectUser").addEventListener("change", function () {
                    loadUserConfig(page, this.value);
                });

                page.querySelector("#embedColor").addEventListener("change", function () {
                    page.querySelector("#txtEmbedColor").value = this.value;
                });

                page.querySelector("#txtEmbedColor").addEventListener("input", function () {
                    page.querySelector("#embedColor").value = this.value;
                });

                page.querySelector("#btnTestNotification").addEventListener("click", function () {
                    testNotification(page);
                });
            });
        }
});