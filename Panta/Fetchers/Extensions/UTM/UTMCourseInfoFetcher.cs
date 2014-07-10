﻿using Panta.DataModels;
using Panta.DataModels.Extensions.UT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Panta.Fetchers.Extensions.UTM
{
    public class UTMCourseInfoFetcher : WebpageItemFetcher<UTCourse>
    {
        public UTMCourseInfoFetcher(string dep, string url)
            : base(url)
        {
            this.Department = dep;
        }

        public string Department { get; set; }

        private static Regex TableRegex, TimeRegex, AngleRegex, CircleRegex;

        static UTMCourseInfoFetcher()
        {
            TableRegex = new Regex("<span id='(?<code>[A-Z]{3}[0-9]{3})(?<prefix>[HY][0-9])(?<semester>([FSY]))'.*?title='(?<detail>[^']*)'.*?[A-Z]{3}[0-9]{3}[HY][0-9][FSY] - (?<name>.*?)</span>(?<content>.*?)((</table>)|($))", RegexOptions.Compiled);
            TimeRegex = new Regex("[0-9]{2}:[0-9]{2}", RegexOptions.Compiled);
            AngleRegex = new Regex("<[^>]*>", RegexOptions.Multiline | RegexOptions.Compiled);
            CircleRegex = new Regex("[\u0020]*[\u0028][^\u0029]*[\u0029][\u0020]*", RegexOptions.Compiled);
        }

        public override IEnumerable<UTCourse> FetchItems()
        {
            List<UTCourse> results = new List<UTCourse>();
            this.Content = this.Content.Replace("\n", String.Empty).Replace("\r", String.Empty);
            MatchCollection courseMatches = TableRegex.Matches(this.Content);
            foreach (Match courseMatch in courseMatches)
            {
                UTCourse course = new UTCourse()
                {
                    Department = this.Department,
                    Code = courseMatch.Groups["code"].Value,
                    SemesterPrefix = courseMatch.Groups["prefix"].Value,
                    Semester = courseMatch.Groups["semester"].Value,
                    Sections = new List<CourseSection>(),
                    Campus = "UTM"
                };

                Console.Write("UTM Course: " + course.Abbr);

                // Process name
                string name = HttpUtility.HtmlDecode(courseMatch.Groups["name"].Value).Replace("<esp233>", "é") ;
                if (name.Contains("SSc"))
                {
                    course.AddCategory("SSc");
                }
                if (name.Contains("SCI"))
                {
                    course.AddCategory("SCI");
                }
                if (name.Contains("HUM"))
                {
                    course.AddCategory("HUM");
                }
                course.Name = CircleRegex.Replace(name, String.Empty).Trim(' ');

                // Parse detail
                string[] details = AngleRegex.Replace(courseMatch.Groups["detail"].Value.Replace("<br>", "|"), String.Empty).Split('|');
                course.Description = HttpUtility.HtmlDecode(details[0]);
                foreach (string detail in details)
                {
                    if (detail.StartsWith("Exclusion"))
                    {
                        course.Exclusions = detail.Substring("Exclusion: ".Length);
                    }
                    else if (detail.StartsWith("Prerequisite"))
                    {
                        course.Prerequisites = detail.Substring("Prerequisites: ".Length);
                    }
                    else if (detail.StartsWith("Corequisite"))
                    {
                        course.Corequisites = detail.Substring("Corequisites: ".Length);
                    }
                }

                // Process sections and meettime
                string courseContent = courseMatch.Groups["content"].Value;
                string[] sectionContents = courseContent.Replace("</tr>", "|").Split('|');

                int nameColumn = 0, instructorColumn = 0, dayColumn = 0, startColumn = 0, endColumn = 0, locationColumn = 0;
                int count = 0;

                // Assign column number
                foreach (string column in AngleRegex.Replace(sectionContents[0].Replace("</th>", "|"), String.Empty).Split('|'))
                {
                    switch (column)
                    {
                        case ("Section"):
                            {
                                nameColumn = count;
                                break;
                            }
                        case ("Instructor"):
                            {
                                instructorColumn = count;
                                break;
                            }
                        case ("Day"):
                            {
                                dayColumn = count;
                                break;
                            }
                        case ("Start"):
                            {
                                startColumn = count;
                                break;
                            }
                        case ("End"):
                            {
                                endColumn = count;
                                break;
                            }
                        case ("Room"):
                            {
                                locationColumn = count;
                                break;
                            }
                    }
                    count++;
                }

                // Parse section
                foreach (string sectionContent in sectionContents)
                {
                    UTCourseSection section = new UTCourseSection();
                    string[] meetTimeContent = sectionContent.Replace("</td>", "|").Split('|');
                    if (meetTimeContent.Length < 4) continue;
                    int meetTimeCount = meetTimeContent[dayColumn].Replace("<br>", "|").Split('|').Length;

                    // Pre-initialize the meet times
                    CourseSectionTime time = new CourseSectionTime();
                    List<CourseSectionTimeSpan> meets = new List<CourseSectionTimeSpan>();
                    for (int i = 0; i < meetTimeCount; i++)
                    {
                        meets.Add(new CourseSectionTimeSpan());
                    }

                    for (int i = 0; i < meetTimeContent.Length; i++)
                    {
                        // Section name
                        if (i == nameColumn)
                        {
                            section.Name = AngleRegex.Replace(meetTimeContent[i], String.Empty);
                        }
                        // Instructor
                        else if (i == instructorColumn)
                        {
                            section.Instructor = AngleRegex.Replace(meetTimeContent[i].Replace("<br>", " "), String.Empty);
                        }
                        // Day
                        else if (i == dayColumn)
                        {
                            string[] days = AngleRegex.Replace(meetTimeContent[i].Replace("<br>", "|"), String.Empty).Split('|');

                            for (int j = 0; j < days.Length; j++)
                            {
                                CourseSectionTimeSpan span = meets[j];
                                span.Day = UTMTimeParser.ParseDay(days[j]);
                                meets[j] = span;
                            }
                        }
                        // Start time
                        else if (i == startColumn)
                        {
                            int meetCount = 0;
                            foreach (string rawTime in AngleRegex.Replace(meetTimeContent[i].Replace("<br>", "|"), String.Empty).Split('|'))
                            {
                                byte startTime;
                                UTCourseSectionTimeSpan.TryParseTimeSpanInt(rawTime, out startTime);
                                CourseSectionTimeSpan span = meets[meetCount];
                                span.Start = startTime;
                                meets[meetCount] = span;
                                meetCount++;
                            }
                        }
                        // End time
                        else if (i == endColumn)
                        {
                            int meetCount = 0;
                            foreach (string rawTime in AngleRegex.Replace(meetTimeContent[i].Replace("<br>", "|"), String.Empty).Split('|'))
                            {
                                byte endTime;
                                UTCourseSectionTimeSpan.TryParseTimeSpanInt(rawTime, out endTime);
                                CourseSectionTimeSpan span = meets[meetCount];
                                span.End = endTime;
                                meets[meetCount] = span;
                                meetCount++;
                            }
                        }
                        // Location
                        else if (i == locationColumn)
                        {
                            section.Location = AngleRegex.Replace(meetTimeContent[i].Replace("<br>", "|"), String.Empty).Replace(" ", String.Empty).Replace("|", " ");
                        }
                    }
                    time.MeetTimes = meets;
                    section.ParsedTime = time;
                    course.Sections.Add(section);
                    Console.Write(" " + section.Name);
                }
                Console.WriteLine();
                results.Add(course);
            }
            return results;
        }
    }
}
