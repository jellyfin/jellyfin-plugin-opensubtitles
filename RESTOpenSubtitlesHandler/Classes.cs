using System;
using System.Net;
using System.Collections.Generic;

namespace RESTOpenSubtitlesHandler {
    public class APIResponse<T>
    {
        public int code;
        public string body = string.Empty;
        public int remaining, reset;
        public Dictionary<string, string> headers;
        public T data;

        public APIResponse((T, (int, int), Dictionary<string, string>, HttpStatusCode) obj)
        {
            this.headers = obj.Item3;
            this.remaining = obj.Item2.Item1;
            this.reset = obj.Item2.Item2;
            this.code = (int) obj.Item4;
            this.data = obj.Item1;
        }

        public APIResponse((string, (int, int), Dictionary<string, string>, HttpStatusCode) obj)
        {
            this.body = obj.Item1;
            this.headers = obj.Item3;
            this.remaining = obj.Item2.Item1;
            this.reset = obj.Item2.Item2;
            this.code = (int) obj.Item4;

            if (typeof(T) == typeof(string))
            {
                this.data = (T)(object)this.body;
                return;
            }

            if (!this.OK)
            {
                //don't bother parsing json if HTTP status code is bad 
                return;
            }

            try
            {
                this.data = Util.Deserialize<T>(this.body);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to parse JSON: " + e.Message + "\n\n" + this.body);
            }
        }

        public bool OK
        {
            get
            {
                return code < 400;
            }
        }
    }

    public class ResponseObjects
    {
        public class UserInfo
        {
            public int allowed_downloads;
            public string level;
            public int user_id;
            public bool ext_installed;
            public bool vip;
            public int? downloads_count;
            public int? remaining_downloads;
        }

        public class EncapsulatedUserInfo
        {
            public UserInfo data;
        }

        public class LoginInfo
        {
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
            public int? year;
            public string title;
            public string movie_name;
            public int imdb_id;
            public int? tmdb_id;
            public int? season_number;
            public int? episode_number;
            public int? parent_imdb_id;
            public string parent_title;
            public int? parent_tmdb_id;
            public int? parent_feature_id;
        }

        public class SubFile
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
            public string format;
            public double fps;
            public int votes;
            public int points;
            public double ratings;
            public bool? from_trusted;
            public bool foreign_parts_only;
            public bool auto_translation;
            public bool ai_translated;
            public object machine_translated;
            public DateTime upload_date;
            public string release;
            public string comments;
            public int? legacy_subtitle_id;
            public Uploader uploader;
            public FeatureDetails feature_details;
            public string url;
            public List<SubFile> files;
            public bool? moviehash_match;
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
            public string page;
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
}