using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Serilog;
using MimeMapping;
using CommonUtils;
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Globalization;

namespace FindSimilarClient
{
    public class MultipartFileSender : FileStreamResult
    {
        private const int DEFAULT_BUFFER_SIZE = 20480; // ..bytes = 20KB.
        private const long DEFAULT_EXPIRE_TIME_SECONDS = 604800L; // ..seconds = 1 week.
        private const string MULTIPART_BOUNDARY = "MULTIPART_BYTERANGES";
        private const string CrLf = "\r\n";

        private string filePath;

        private MultipartFileSender(Stream fileStream, string contentType)
            : base(fileStream, contentType)
        {
        }

        private MultipartFileSender(Stream fileStream, MediaTypeHeaderValue contentType)
            : base(fileStream, contentType)
        {
        }

        public static MultipartFileSender FromFile(FileInfo file, MediaTypeHeaderValue contentType)
        {
            return new MultipartFileSender(new FileStream(file.FullName, FileMode.Open, FileAccess.Read), contentType).SetFilePath(file.FullName);
            // return new MultipartFileSender(File.OpenRead(file.FullName), contentType).SetFilePath(file.FullName);
        }

        public static MultipartFileSender FromFile(string filePath, string contentType)
        {
            return new MultipartFileSender(new FileStream(filePath, FileMode.Open, FileAccess.Read), contentType).SetFilePath(filePath);
            // return new MultipartFileSender(File.OpenRead(filePath), contentType).SetFilePath(filePath);
        }

        public static MultipartFileSender FromStream(Stream stream, MediaTypeHeaderValue contentType)
        {
            return new MultipartFileSender(stream, contentType);
        }

        public static MultipartFileSender FromStream(Stream stream, string contentType)
        {
            return new MultipartFileSender(stream, contentType);
        }

        private MultipartFileSender SetFilePath(string filepath)
        {
            this.filePath = filepath;
            return this;
        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            await ServeResource(context.HttpContext.Response);
        }

        private void LogRequestHeaders(HttpResponse response)
        {
            if (Debugger.IsAttached)
            {
                string headers = String.Empty;
                foreach (var key in response.HttpContext.Request.Headers.Keys)
                {
                    headers += key + "=" + response.HttpContext.Request.Headers[key] + Environment.NewLine;
                }
                Log.Debug("----Request Headers----\n" + headers);
            }
        }

        private void LogResponseHeaders(HttpResponse response)
        {
            if (Debugger.IsAttached)
            {
                string headers = "StatusCode: " + response.StatusCode.ToString() + Environment.NewLine;
                foreach (var key in response.Headers.Keys)
                {
                    headers += key + "=" + response.Headers[key] + Environment.NewLine;
                }
                Log.Debug("----Response Headers----\n" + headers);
            }
        }

        public async Task ServeResource(HttpResponse response)
        {
            if (response == null)
            {
                return;
            }

            LogRequestHeaders(response);


            // Read all the file properties needed ---------------------------------------------------
            // the file-name and last modified date
            // and the content-type (mime mapping)

            if (!File.Exists(filePath))
            {
                Log.Error("FileInfo doesn't exist at URI : {0}", filePath);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            long length = new FileInfo(filePath).Length;
            string fileName = StringUtils.RemoveNonAsciiCharactersFast(Path.GetFileName(filePath));
            DateTime lastModifiedObj = File.GetLastWriteTime(filePath);

            if (string.IsNullOrEmpty(fileName) || lastModifiedObj == null)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            DateTimeOffset? lastModifiedDTO = DateTime.SpecifyKind(lastModifiedObj, DateTimeKind.Utc);

            // Since the 'Last-Modified' and other similar http date headers are rounded down to whole seconds, 
            // round down current file's last modified to whole seconds for correct comparison. 
            if (lastModifiedDTO.HasValue)
            {
                lastModifiedDTO = RoundDownToWholeSeconds(lastModifiedDTO.Value);
            }
            long lastModified = lastModifiedDTO.Value.ToUnixTimeSeconds();

            string contentType = MimeMapping.MimeUtility.GetMimeMapping(filePath);


            // Validate request headers for caching ---------------------------------------------------

            // If-None-Match header should contain "*" or ETag. If so, then return 304.
            string ifNoneMatch = response.HttpContext.Request.Headers["If-None-Match"];
            if (ifNoneMatch != null && HttpUtils.Matches(ifNoneMatch, fileName))
            {
                response.Headers.Add("ETag", fileName); // Required in 304.
                response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            // If-Modified-Since header should be greater than LastModified. If so, then return 304.
            // This header is ignored if any If-None-Match header is specified.
            long ifModifiedSince = GetDateHeader(response, "If-Modified-Since");
            if (ifNoneMatch == null && ifModifiedSince != -1 && ifModifiedSince + 1000 > lastModified)
            {
                response.Headers.Add("ETag", fileName); // Required in 304.
                response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            // Validate request headers for resume ----------------------------------------------------

            // If-Match header should contain "*" or ETag. If not, then return 412.
            string ifMatch = response.HttpContext.Request.Headers["If-Match"];
            if (ifMatch != null && !HttpUtils.Matches(ifMatch, fileName))
            {
                response.StatusCode = (int)HttpStatusCode.PreconditionFailed;
                return;
            }

            // If-Unmodified-Since header should be greater than LastModified. If not, then return 412.
            long ifUnmodifiedSince = GetDateHeader(response, "If-Unmodified-Since");
            if (ifUnmodifiedSince != -1 && ifUnmodifiedSince + 1000 <= lastModified)
            {
                response.StatusCode = (int)HttpStatusCode.PreconditionFailed;
                return;
            }

            // Validate and process range -------------------------------------------------------------

            // Prepare some variables. The full Range represents the complete file.
            Range full = new Range(0, length - 1, length);
            List<Range> ranges = new List<Range>();

            // Validate and process Range and If-Range headers.
            Regex rangeRegex = new Regex(@"^bytes=\d*-\d*(,\d*-\d*)*$");
            string range = response.HttpContext.Request.Headers["Range"];
            if (range != null)
            {
                // Range header should match format "bytes=n-n,n-n,n-n...". If not, then return 416.
                if (!rangeRegex.IsMatch(range))
                {
                    response.Headers.Add("Content-Range", "bytes */" + length); // Required in 416.
                    response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    return;
                }

                string ifRange = response.HttpContext.Request.Headers["If-Range"];
                if (ifRange != null && !ifRange.Equals(fileName))
                {
                    long ifRangeTime = GetDateHeader(response, "If-Range");
                    if (ifRangeTime != -1)
                    {
                        ranges.Add(full);
                    }
                }

                // If any valid If-Range header, then process each part of byte range.
                if (ranges.Count == 0)
                {
                    // Remove "Ranges" and break up the ranges
                    string[] rangeArray = range.Replace("bytes=", string.Empty)
                                                 .Split(",".ToCharArray());

                    foreach (string part in rangeArray)
                    {
                        // Assuming a file with length of 100, the following examples returns bytes at:
                        // 50-80 (50 to 80), 40- (40 to length=100), -20 (length-20=80 to length=100).
                        long start = Range.SubLong(part, 0, part.IndexOf("-"));
                        long end = Range.SubLong(part, part.IndexOf("-") + 1, part.Length);

                        if (start == -1)
                        {
                            start = length - end;
                            end = length - 1;
                        }
                        else if (end == -1 || end > length - 1)
                        {
                            end = length - 1;
                        }

                        // Check if Range is syntactically valid. If not, then return 416.
                        if (start > end)
                        {
                            response.Headers.Add("Content-Range", "bytes */" + length); // Required in 416.
                            response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                            return;
                        }

                        // Add range.                    
                        ranges.Add(new Range(start, end, length));
                    }
                }
            }

            // Prepare and Initialize response --------------------------------------------------------

            // disable response buffering
            var bufferingFeature = response.HttpContext.Features.Get<IHttpBufferingFeature>();
            bufferingFeature?.DisableResponseBuffering();

            // Get content type by file name and Set content disposition.
            string disposition = "inline";

            // If content type is unknown, then Set the default value.
            // For all content types, see: http://www.w3schools.com/media/media_mimeref.asp
            // To add new content types, add new mime-mapping entry in web.xml.
            if (contentType == null)
            {
                contentType = "application/octet-stream";
            }
            else if (!contentType.StartsWith("image"))
            {
                // Else, expect for images, determine content disposition. If content type is supported by
                // the browser, then Set to inline, else attachment which will pop a 'save as' dialogue.
                string accept = response.HttpContext.Request.Headers["Accept"];
                disposition = accept != null && HttpUtils.Accepts(accept, contentType) ? "inline" : "attachment";
            }

            Log.Debug("Content-Type : {0}", contentType);

            // Initialize response.
            try
            {
                response.Headers.Add("Content-Type", contentType);
                response.Headers.Add("Content-Disposition", disposition + ";filename=\"" + fileName + "\"");
                Log.Debug("Content-Disposition : {0}", disposition);

                response.Headers.Add("Accept-Ranges", "bytes");

                // Check SetLastModifiedAndEtagHeaders() in FileResultExecutorBase.cs for info about adding headers
                response.Headers.Add("ETag", fileName);
                response.Headers.Add("Last-Modified", lastModifiedDTO.Value.ToString("r", CultureInfo.InvariantCulture));

                // set expiration header (remove milliseconds)
                var expiresValue = DateTimeOffset
                                .UtcNow
                                .AddSeconds(DEFAULT_EXPIRE_TIME_SECONDS)
                                .ToString("r", CultureInfo.InvariantCulture);

                response.Headers.Add("Expires", expiresValue);
            }
            catch (System.Exception e)
            {
                Log.Error("Failed adding response headers: {0}", e.Message);
            }

            // Send requested file (part(s)) to client ------------------------------------------------

            // Prepare streams.
            Stream input = FileStream;
            Stream output = response.Body;

            if (ranges.Count == 0 || ranges[0] == full)
            {
                // Return full file.
                Log.Information("Return full file");
                response.ContentType = contentType;
                response.Headers.Add("Content-Range", "bytes " + full.Start + "-" + full.End + "/" + full.Total);
                response.Headers.Add("Content-Length", full.Length.ToString());

                LogResponseHeaders(response);

                await Range.Copy(input, output, length, full.Start, full.Length);
            }
            else if (ranges.Count == 1)
            {
                // Return single part of file.
                Range r = ranges[0];
                Log.Information("Return 1 part of file : from ({0}) to ({1})", r.Start, r.End);
                response.ContentType = contentType;
                response.Headers.Add("Content-Range", "bytes " + r.Start + "-" + r.End + "/" + r.Total);
                response.Headers.Add("Content-Length", r.Length.ToString());
                response.StatusCode = (int)HttpStatusCode.PartialContent; // 206

                LogResponseHeaders(response);

                // Copy single part range.
                await Range.Copy(input, output, length, r.Start, r.Length);
            }
            else
            {
                // Return multiple parts of file.
                response.ContentType = "multipart/byteranges; boundary=" + MULTIPART_BOUNDARY;
                response.StatusCode = (int)HttpStatusCode.PartialContent; // 206

                LogResponseHeaders(response);

                // Copy multi part range.
                foreach (Range r in ranges)
                {
                    Log.Information("Return multi part of file : from ({0}) to ({1})", r.Start, r.End);

                    // Add multipart boundary and header fields for every range.
                    await response.WriteAsync(CrLf);
                    await response.WriteAsync("--" + MULTIPART_BOUNDARY);
                    await response.WriteAsync(CrLf);
                    await response.WriteAsync("Content-Type: " + contentType);
                    await response.WriteAsync(CrLf);
                    await response.WriteAsync("Content-Range: bytes " + r.Start + "-" + r.End + "/" + r.Total);
                    await response.WriteAsync(CrLf);

                    // Copy single part range of multi part range.
                    await Range.Copy(input, output, length, r.Start, r.Length);
                }

                // End with multipart boundary.
                await response.WriteAsync(CrLf);
                await response.WriteAsync("--" + MULTIPART_BOUNDARY + "--");
                await response.WriteAsync(CrLf);
            }
        }

        private static DateTimeOffset RoundDownToWholeSeconds(DateTimeOffset dateTimeOffset)
        {
            var ticksToRemove = dateTimeOffset.Ticks % TimeSpan.TicksPerSecond;
            return dateTimeOffset.Subtract(TimeSpan.FromTicks(ticksToRemove));
        }

        private static long GetDateHeader(HttpResponse response, string header)
        {
            var headerValue = response.HttpContext.Request.Headers[header].ToString();

            if (string.IsNullOrEmpty(headerValue)) return -1;

            DateTimeOffset parsedDateOffset;
            DateTimeOffset.TryParseExact(
                                headerValue,
                                "r",
                                CultureInfo.InvariantCulture.DateTimeFormat,
                                DateTimeStyles.AdjustToUniversal,
                                out parsedDateOffset);

            return parsedDateOffset.ToUnixTimeSeconds();
        }

        private class Range
        {
            public long Start;
            public long End;
            public long Length;
            public long Total;

            /// <summary>
            /// Construct a byte range.
            /// </summary>
            /// <param name="start">Start of the byte range.</param>
            /// <param name="end">End of the byte range.</param>
            /// <param name="total">Total length of the byte source.</param>
            public Range(long start, long end, long total)
            {
                this.Start = start;
                this.End = end;
                this.Length = end - start + 1;
                this.Total = total;
            }

            public static long SubLong(string value, int beginIndex, int endIndex)
            {
                string substring;
                try
                {
                    substring = value.Substring(beginIndex, endIndex);
                    return (substring.Length > 0) ? long.Parse(substring) : -1;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return -1;
                }
            }

            public static async Task Copy(Stream input, Stream output, long inputSize, long start, long length)
            {
                byte[] buffer = new byte[DEFAULT_BUFFER_SIZE];
                int bytesRead;

                if (inputSize == length)
                {
                    try
                    {
                        // Write full range.
                        while ((bytesRead = input.Read(buffer)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, bytesRead);
                            await output.FlushAsync();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Log.Error(e.Message);
                    }
                }
                else
                {
                    input.Seek(start, SeekOrigin.Begin);
                    long toRead = length;
                    try
                    {
                        while ((bytesRead = input.Read(buffer)) > 0)
                        {
                            if ((toRead -= bytesRead) > 0)
                            {
                                await output.WriteAsync(buffer, 0, bytesRead);
                                await output.FlushAsync();
                            }
                            else
                            {
                                await output.WriteAsync(buffer, 0, (int)toRead + bytesRead);
                                await output.FlushAsync();
                                break;
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Log.Error(e.Message);
                    }
                }
            }
        }
        private static class HttpUtils
        {
            /// <summary>
            /// Returns true if the given accept header accepts the given value.
            /// </summary>
            /// <param name="acceptHeader">The accept header.</param>
            /// <param name="toAccept">The value to be accepted.</param>
            /// <returns>True if the given accept header accepts the given value.</returns>
            public static bool Accepts(string acceptHeader, string toAccept)
            {
                string[] acceptValues = Regex.Split(acceptHeader, @"\s*(,|;)\s*");
                Array.Sort(acceptValues);

                Regex rgx = new Regex(@"/.*$");
                return Array.BinarySearch(acceptValues, toAccept) > -1
                        || Array.BinarySearch(acceptValues, rgx.Replace(toAccept, "/*")) > -1
                        || Array.BinarySearch(acceptValues, "*/*") > -1;
            }

            /// <summary>
            /// Returns true if the given match header matches the given value.
            /// </summary>
            /// <param name="matchHeader">The match header.</param>
            /// <param name="toMatch">The value to be matched.</param>
            /// <returns>True if the given match header matches the given value.</returns>
            public static bool Matches(string matchHeader, string toMatch)
            {
                string[] matchValues = Regex.Split(matchHeader, @"\s*,\s*");
                Array.Sort(matchValues);

                return Array.BinarySearch(matchValues, toMatch) > -1
                        || Array.BinarySearch(matchValues, "*") > -1;
            }
        }
    }
}
