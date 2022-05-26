using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamDownloader
{
    class Program
    {
        static List<ulong> Files = new List<ulong>();
        static string SteamId;
        static string DownloadFolder => $"Screenshots{SteamId}";

        static Dictionary<string, string> MimeToExtension = new Dictionary<string, string>()
        {
            { "image/jpeg", ".jpg" },
            { "image/png", ".png" },
            { "image/gif", ".gif" },
            { "image/webp", ".webp" },
        };

        static async Task Main( string[] args )
        {
            Console.WriteLine( "Hello, want to download some screenshots yeah?" );
            Console.WriteLine();
            Console.WriteLine( "Your SteamId is a long number, usually starting with 7.. ie 76561197960279927" );
            Console.WriteLine( "You can paste in this window by right clicking" );

            AskForSteamId:

            Console.WriteLine();
            Console.WriteLine( "Please type your SteamId:" );

            SteamId = Console.ReadLine().Trim();

            if ( !long.TryParse( SteamId, out var _ ) )
            {
                Console.Write( "That doesn't look right." );
                goto AskForSteamId;
            }

            // Make sure the folder exists
            if ( !System.IO.Directory.Exists( DownloadFolder ) )
                System.IO.Directory.CreateDirectory( DownloadFolder );

            // Scan all the pages for files
            await ScanPages();

            // Remove any duplicates
            Files = Files.Distinct().ToList();

            if ( Files.Count == 0 )
            {
                Console.WriteLine();
                Console.WriteLine( "No screenshots found. Is the profile set to private?" );
                return;
            }

            Console.WriteLine( $"Found {Files.Count()} Screenshots" );

            // Download them all
            await DownloadImages();

            Console.WriteLine();
            Console.WriteLine( $"All done! You can see the screenshots in \"/{DownloadFolder}/\"" );
        }

        static async Task ScanPages()
        {
            int page = 1;
            while ( true )
            {
                Console.WriteLine( $"Getting Page {page} ({Files.Count} screenshots found)" );

                int fails = 0;
                while ( !await GetPage( page, SteamId ) )
                {
                    fails++;
                    Console.WriteLine( $"Page {page} didn't have any screenshots" );

                    if ( fails > 3 )
                    {
                        Console.WriteLine( $"I think we're at the end. Lets start grabbing the screenshots." );
                        return;
                    }

                    Console.WriteLine( $"Retrying incase it was a server error.." );

                    await Task.Delay( fails * 1000 );
                } 

                await Task.Delay( 100 );
                page++;
            }
        }

        private static async Task<bool> GetPage( int pageid, string targetAccount )
        {
            try
            {
                using ( var client = new HttpClient() )
                {
                    var response = await client.GetStringAsync( $"https://steamcommunity.com/profiles/{targetAccount}/screenshots?p={pageid}&browsefilter=myfiles&view=grid&privacy=30" );

                    var matches = Regex.Matches( response, "steamcommunity\\.com/sharedfiles/filedetails/\\?id\\=([0-9]+?)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase );

                    if ( matches.Count == 0 )
                        return false;

                    foreach ( Match match in matches )
                    {
                        Files.Add( ulong.Parse( match.Groups[1].Value ) );
                    }
                }

                return true;
            }
            catch ( System.Exception e )
            {
                Console.Error.WriteLine( e.Message );
                return false;
            }
        }

        static async Task DownloadImages()
        {
            var tasks = new List<Task>();

            foreach ( var file in Files )
            {
                var t = DownloadImage( file );

                tasks.Add( t );

                while ( tasks.Count > 16 )
                {
                    await Task.WhenAny( tasks );
                    tasks.RemoveAll( x => x.IsCompleted );
                }
            }

            await Task.WhenAll( tasks );
        }

        private static async Task<bool> DownloadImage( ulong file )
        {
            int tries = 0;

            Retry:

            tries++;

            try
            {
                using ( var client = new HttpClient() )
                {
                    client.DefaultRequestHeaders.Add( "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.132 Safari/537.36" );

                    var response = await client.GetStringAsync( $"https://steamcommunity.com/sharedfiles/filedetails/?id={file}" );

                    var matches = Regex.Matches( response, "\\<a href\\=\"(https\\://steamuserimages-a.akamaihd.net/ugc/([A-Z0-9/].+?))\"", RegexOptions.Singleline | RegexOptions.IgnoreCase );

                    if ( matches.Count == 0 )
                    {
                        Console.WriteLine( $"[{file}] - couldn't find image link" );
                        return false;
                    }

                    var imageUrl = matches.First().Groups[1].Value;

                    Console.WriteLine( $"[{file}] - downloading {imageUrl}" );

                    var download = await client.GetAsync( imageUrl );

                    var fileId = GetStringBetween(imageUrl, "ugc/", "/");
                    var extension = GetFileExtension(download.Content.Headers.GetValues("Content-Type").First());
                    var data = await download.Content.ReadAsByteArrayAsync();

                    System.IO.File.WriteAllBytes( $"{DownloadFolder}/{fileId}{extension}", data );

                    return true;
                }
            }
            catch ( System.Exception e )
            {
                Console.Error.WriteLine( e.Message );

                if ( tries < 3 )
                {
                    await Task.Delay( 3000 * tries );
                    goto Retry;
                }

                return false;
            }
        }

        private static string GetStringBetween(string s, string startWord, string endWord)
        { 
            int Pos1 = s.IndexOf(startWord) + startWord.Length;
            int Pos2 = s.IndexOf(endWord, Pos1);
            return s.Substring(Pos1, Pos2 - Pos1);
        }

        private static string GetFileExtension(string mimeType)
        {
            MimeToExtension.TryGetValue(mimeType, out string extension);
            if (extension == null)
            {
                throw new ArgumentException($"Mimetype: {mimeType} not found in the dictionary!");
            }
            return extension;
        }
    }
}
