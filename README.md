<h1 align="center">Jellyfin OpenSubtitles Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org">Jellyfin Project</a></h3>

<p align="center">
<img alt="Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/jellyfin/jellyfin-plugin-opensubtitles/actions?query=workflow%3A%22Test+Build+Plugin%22">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/workflow/status/jellyfin/jellyfin-plugin-opensubtitles/Test%20Build%20Plugin.svg">
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-opensubtitles">
<img alt="MIT License" src="https://img.shields.io/github/license/jellyfin/jellyfin-plugin-opensubtitles.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-opensubtitles/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/jellyfin/jellyfin-plugin-opensubtitles.svg"/>
</a>
</p>

## About
This is a plugin allows you to download subtitles from [Open Subtitles](https://opensubtitles.org) for your media.

## Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command

```sh
dotnet publish --configuration Release --output bin
```
