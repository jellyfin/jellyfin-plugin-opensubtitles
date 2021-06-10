using System;
using System.Collections.Generic;

namespace RESTOpenSubtitlesHandler {
    public class UserInfo {
        public int allowed_downloads;
        public string level;
        public int user_id;
        public bool ext_installed;
        public bool vip;
    }

    public class LoginInfo {
        public UserInfo user;
        public string token;
        public int status;
    }

    public class Uploader
    {
        public int? uploader_id;
        public string name;
        public string rank;
    }

    public class FeatureDetails
    {
        public int feature_id;
        public string feature_type;
        public int year;
        public string title;
        public string movie_name;
        public int imdb_id;
        public int tmdb_id;
    }

    public class RelatedLinks
    {
        public string label;
        public string url;
        public string img_url;
    }

    public class File
    {
        public int file_id;
        public int cd_number;
        public string file_name;
    }

    public class Attributes
    {
        public string subtitle_id;
        public string language;
        public int download_count;
        public int new_download_count;
        public bool hearing_impaired;
        public bool hd;
        public object format;
        public double fps;
        public int votes;
        public int points;
        public double ratings;
        public bool from_trusted;
        public bool foreign_parts_only;
        public bool auto_translation;
        public bool ai_translated;
        public object machine_translated;
        public DateTime upload_date;
        public string release;
        public string comments;
        public int legacy_subtitle_id;
        public Uploader uploader;
        public FeatureDetails feature_details;
        public string url;
        public RelatedLinks related_links;
        public List<File> files;
    }

    public class Data
    {
        public string id;
        public string type;
        public Attributes attributes;
    }

    public class SearchResult
    {
        public int total_pages;
        public int total_count;
        public int page;
        public List<Data> data;
    }

    public class SubtitleDownloadInfo
    {
        public string link;
        public string file_name;
        public int requests;
        public int remaining;
        public object message;
    }
}