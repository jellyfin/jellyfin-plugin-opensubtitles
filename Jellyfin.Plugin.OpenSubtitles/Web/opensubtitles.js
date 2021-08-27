const OpenSubtitlesConfig = {
    pluginUniqueId: '4b9ed42f-5185-48b5-9803-6ff2989014c4'
};

export default function (view, params) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            page.querySelector('#username').value = config.Username || '';
            page.querySelector('#password').value = config.Password || '';
            page.querySelector('#apikey').value = config.ApiKey || '';
            Dashboard.hideLoadingMsg();
        });
    });

    view.querySelector('#OpenSubtitlesConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;
        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            const username = form.querySelector('#username').value;
            const password = form.querySelector('#password').value;
            const apiKey = form.querySelector('#apikey').value;

            if (!username || !password || !apiKey) {
                Dashboard.processErrorResponse({statusText: "Account info is incomplete"});
                return;
            }

            const el = form.querySelector('#ossresponse');
            
            const data = JSON.stringify({ Username: username, Password: password, ApiKey: apiKey });
            const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo');

            const handler = response => response.json().then(res => {
                if (response.ok) {
                    el.innerText = `Login info validated, this account can download ${res.Downloads} subtitles per day`;

                    config.Username = username;
                    config.Password = password;
                    config.ApiKey = apiKey;

                    ApiClient.updatePluginConfiguration(OpenSubtitlesConfig.pluginUniqueId, config).then(function (result) {
                        Dashboard.processPluginConfigurationUpdateResult(result);
                    });
                }
                else {
                    let msg = res.Message ?? JSON.stringify(res, null, 2);

                    if (msg == 'You cannot consume this service') {
                        msg = 'Invalid API key provided';
                    }

                    Dashboard.processErrorResponse({statusText: `Request failed - ${msg}`});
                }
            });

            ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json'}).then(handler).catch(handler);
        });
        return false;
    });
}
