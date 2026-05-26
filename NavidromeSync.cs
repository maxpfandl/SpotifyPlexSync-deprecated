using System.Web;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SpotifyAPI.Web;

namespace SpotifyPlexSync
{
    public class NavidromeSync
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private HttpClient _client;

        public NavidromeSync(IConfiguration config, ILogger logger, HttpClient client)
        {
            _config = config;
            _logger = logger;
            _client = client;
        }

        private string GetAuthParams()
        {
            var username = _config?["Navidrome:Username"];
            var password = _config?["Navidrome:Password"];
            return $"u={username}&p={password}&c=SpotifyPlexSync&f=json";
        }

        public async Task<bool> CheckNavidromeRunning()
        {
            try
            {
                var url = $"{_config?["Navidrome:Url"]}/rest/ping.view?{GetAuthParams()}";
                var result = await _client.GetAsync(url);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError("Navidrome seems to be unavailable");
                    return false;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                if (json?["subsonic-response"]?["status"]?.ToString() == "ok")
                {
                    return true;
                }

                _logger?.LogError("Navidrome returned non-ok status");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to check Navidrome connection: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> GetPlaylistId(string title)
        {
            try
            {
                _logger?.LogInformation("Search for Playlist in Navidrome: " + title);
                var url = $"{_config?["Navidrome:Url"]}/rest/getPlaylists.view?{GetAuthParams()}";
                var result = await _client.GetAsync(url);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error fetching playlists from Navidrome: {result.ReasonPhrase}");
                    return null;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var playlists = json["subsonic-response"]?["playlists"]?["playlist"] as JArray;

                if (playlists == null)
                    return null;

                foreach (var playlist in playlists)
                {
                    if (playlist["name"]?.ToString() == title)
                        return playlist["id"]?.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error searching for playlist in Navidrome: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> CreatePlaylist(string name)
        {
            try
            {
                var encodedName = HttpUtility.UrlEncode(name);
                var url = $"{_config?["Navidrome:Url"]}/rest/createPlaylist.view?name={encodedName}&{GetAuthParams()}";
                var result = await _client.PostAsync(url, null);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error creating playlist in Navidrome: {result.ReasonPhrase}");
                    return null;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var playlistId = json["subsonic-response"]?["playlist"]?["id"]?.ToString();

                return playlistId;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error creating playlist in Navidrome: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeletePlaylist(string playlistId)
        {
            try
            {
                var url = $"{_config?["Navidrome:Url"]}/rest/deletePlaylist.view?id={playlistId}&{GetAuthParams()}";
                var result = await _client.PostAsync(url, null);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error deleting playlist from Navidrome: {result.ReasonPhrase}");
                    return false;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                if (json["subsonic-response"]?["status"]?.ToString() == "ok")
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error deleting playlist from Navidrome: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddTrackToPlaylist(string playlistId, string trackId)
        {
            try
            {
                var url = $"{_config?["Navidrome:Url"]}/rest/addToPlaylist.view?playlistId={playlistId}&songId={trackId}&{GetAuthParams()}";
                var result = await _client.PostAsync(url, null);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error adding track to Navidrome playlist: {result.ReasonPhrase}");
                    return false;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                if (json["subsonic-response"]?["status"]?.ToString() == "ok")
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error adding track to Navidrome playlist: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetPlaylistSongIds(string playlistId)
        {
            var songIds = new List<string>();

            try
            {
                var url = $"{_config?["Navidrome:Url"]}/rest/getPlaylist.view?id={playlistId}&{GetAuthParams()}";
                var result = await _client.GetAsync(url);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error fetching playlist from Navidrome: {result.ReasonPhrase}");
                    return songIds;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var songs = json["subsonic-response"]?["playlist"]?["entry"] as JArray;

                if (songs != null)
                {
                    foreach (var song in songs)
                    {
                        songIds.Add(song["id"]?.ToString()!);
                    }
                }

                return songIds;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error fetching playlist songs from Navidrome: {ex.Message}");
                return songIds;
            }
        }

        public async Task<string?> SearchForTrack(FullTrack spotifyTrack)
        {
            try
            {
                var searchTerm = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Name, @"\(.*?\)", "").Trim();
                searchTerm = System.Text.RegularExpressions.Regex.Replace(searchTerm, @"\[.*?\]", "").Trim();
                searchTerm = spotifyTrack.Artists[0].Name + " " + searchTerm;
                searchTerm = HttpUtility.UrlEncode(searchTerm);

                var url = $"{_config?["Navidrome:Url"]}/rest/search3.view?query={searchTerm}&songCount=100&{GetAuthParams()}";
                var result = await _client.GetAsync(url);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error searching Navidrome for track: {result.ReasonPhrase}");
                    return null;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var songs = json["subsonic-response"]?["searchResult3"]?["song"] as JArray;

                if (songs == null || songs.Count == 0)
                    return null;

                // Try strict match first
                foreach (var song in songs)
                {
                    if (MatchTrackStrict(spotifyTrack, song))
                    {
                        _logger?.LogInformation($"Track found strict on Navidrome: Spotify: {spotifyTrack.Artists[0].Name} - {spotifyTrack.Album.Name} - {spotifyTrack.Name}");
                        return song["id"]?.ToString();
                    }
                }

                // Try fuzzy match
                foreach (var song in songs)
                {
                    if (MatchTrackFuzzy(spotifyTrack, song))
                    {
                        _logger?.LogInformation($"Track found fuzzy on Navidrome: Spotify: {spotifyTrack.Artists[0].Name} - {spotifyTrack.Album.Name} - {spotifyTrack.Name}");
                        return song["id"]?.ToString();
                    }
                }

                _logger?.LogWarning($"Track not found on Navidrome: {spotifyTrack.Artists[0].Name} - {spotifyTrack.Album.Name} - {spotifyTrack.Name}");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error searching for track in Navidrome: {ex.Message}");
                return null;
            }
        }

        private bool MatchTrackStrict(FullTrack spotifyTrack, JToken navidromeTrack)
        {
            var pattern = @"[^0-9a-zA-Z:,]+";
            var spTitle = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Name, pattern, "").ToLower();
            var spArtist = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Artists[0].Name, pattern, "").ToLower();
            var spAlbum = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Album.Name, pattern, "").ToLower();

            var navTitle = System.Text.RegularExpressions.Regex.Replace(navidromeTrack["title"]?.ToString() ?? "", pattern, "").ToLower();
            var navArtist = System.Text.RegularExpressions.Regex.Replace(navidromeTrack["artist"]?.ToString() ?? "", pattern, "").ToLower();
            var navAlbum = System.Text.RegularExpressions.Regex.Replace(navidromeTrack["album"]?.ToString() ?? "", pattern, "").ToLower();

            return spTitle == navTitle && spArtist == navArtist && spAlbum == navAlbum;
        }

        private bool MatchTrackFuzzy(FullTrack spotifyTrack, JToken navidromeTrack)
        {
            var pattern = @"[^0-9a-zA-Z:,]+";
            var spTitle = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Name, pattern, "").ToLower();
            var spArtist = System.Text.RegularExpressions.Regex.Replace(spotifyTrack.Artists[0].Name, pattern, "").ToLower();

            var navTitle = System.Text.RegularExpressions.Regex.Replace(navidromeTrack["title"]?.ToString() ?? "", pattern, "").ToLower();
            var navArtist = System.Text.RegularExpressions.Regex.Replace(navidromeTrack["artist"]?.ToString() ?? "", pattern, "").ToLower();

            if (spTitle == navTitle && spArtist == navArtist)
                return true;

            if (spTitle.Contains(navTitle) && spArtist.Contains(navArtist))
                return true;

            if (navTitle.Contains(spTitle) && navArtist.Contains(spArtist))
                return true;

            return false;
        }

        public async Task<bool> UpdatePlaylistName(string playlistId, string newName)
        {
            try
            {
                var encodedName = HttpUtility.UrlEncode(newName);
                var url = $"{_config?["Navidrome:Url"]}/rest/updatePlaylist.view?playlistId={playlistId}&name={encodedName}&{GetAuthParams()}";
                var result = await _client.PostAsync(url, null);

                if (!result.IsSuccessStatusCode)
                {
                    _logger?.LogError($"Error updating playlist name in Navidrome: {result.ReasonPhrase}");
                    return false;
                }

                var content = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                if (json["subsonic-response"]?["status"]?.ToString() == "ok")
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error updating playlist name in Navidrome: {ex.Message}");
                return false;
            }
        }
    }
}
