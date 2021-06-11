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
            config.Username = form.querySelector('#username').value;
            config.Password = form.querySelector('#password').value;
            config.ApiKey = form.querySelector('#apikey').value;
            ApiClient.updatePluginConfiguration(OpenSubtitlesConfig.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });
        });
        return false;
    });
}
