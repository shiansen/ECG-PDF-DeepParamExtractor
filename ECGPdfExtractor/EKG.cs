using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;

namespace ECGPdfExtractor
{
    class EKG
    {
        private int leadBasePos1;
        private int leadBasePos2;
        private int leadBasePos3;
        private int leadBasePosLong2;

        public Lead I = new Lead();
        public Lead II = new Lead();
        public Lead III = new Lead();
        public Lead longII = new Lead();
        public Lead aVR = new Lead();
        public Lead aVL = new Lead();
        public Lead aVF = new Lead();
        public Lead V1 = new Lead();
        public Lead V2 = new Lead();
        public Lead V3 = new Lead();
        public Lead V4 = new Lead();
        public Lead V5 = new Lead();
        public Lead V6 = new Lead();
        public int Hr { get; private set; }
        public int Pr { get; private set; }
        public int Qrs { get; private set; }
        public int Qt { get; private set; }
        public int Qtc { get; private set; }
        public int Paxis { get; private set; }
        public int Raxis { get; private set; }
        public int Taxis { get; private set; }
        public List<string> Reports {get; private set;}

        private readonly int startTime = 450;    // *** may change on different ECG PDF format
        //private readonly int startRepeat = 5;    // *** may change on different ECG PDF format

        private readonly int timeStartI_II_III = 1080;    // *** may change on different ECG PDF format
        private readonly int timeStartAVRLF = 7330;    // *** may change on different ECG PDF format
        private readonly int timeStartV1_2_3 = 13580;    // *** may change on different ECG PDF format
        private readonly int timeStartV4_5_6 = 19830;    // *** may change on different ECG PDF format


        public EKG(string objectStreamFile)
        {
            using (TextReader reader = File.OpenText(objectStreamFile))
                findLeadBase(reader, objectStreamFile);

            using (TextReader reader = File.OpenText(objectStreamFile))
                findLead(reader, timeStartI_II_III, new int[] { leadBasePos1, leadBasePos2, leadBasePos3, leadBasePosLong2 }, new Lead[] { I, II, III, longII });

            using (TextReader reader = File.OpenText(objectStreamFile))
                findLead(reader, timeStartAVRLF, new int[] { leadBasePos1, leadBasePos2, leadBasePos3 }, new Lead[] { aVR, aVL, aVF });

            using (TextReader reader = File.OpenText(objectStreamFile))
                findLead(reader, timeStartV1_2_3, new int[] { leadBasePos1, leadBasePos2, leadBasePos3 }, new Lead[] { V1, V2, V3 });

            using (TextReader reader = File.OpenText(objectStreamFile))
                findLead(reader, timeStartV4_5_6, new int[] { leadBasePos1, leadBasePos2, leadBasePos3 }, new Lead[] { V4, V5, V6 });

            string txt = File.ReadAllText(objectStreamFile, System.Text.Encoding.GetEncoding("BIG5"));
            Hr = findHr(txt);
            Pr = findPr(txt);
            Qrs = findQrs(txt);
            (Qt, Qtc) = findQt(txt);
            (Paxis, Raxis, Taxis) = findPRT(txt);
            Reports = findReport(txt);
        }

        private void findLeadBase(TextReader reader, string objectStreamFile)
        {
            List<int> basePos = new List<int>();
            while (reader.Peek() > 0)
            {
                string s = reader.ReadLine();
                Regex rx = new Regex(@"^(\d+) (\d+) m$");
                Match m = rx.Match(s);
                if (m.Success)
                {
                    if (m.Groups[1].ToString() == startTime.ToString())
                    {
                        int i = Convert.ToInt32(m.Groups[2].ToString());
                        if (!basePos.Contains(i))
                        {
                            basePos.Add(i);
                            if (basePos.Count == 4)
                                break;
                        }
                    }
                }
            }
            if (basePos.Count != 4)
                throw new Exception(objectStreamFile + ": Error, lead base positions cannot be found !");

            basePos.Sort();
            leadBasePos1 = basePos[3];
            leadBasePos2 = basePos[2];
            leadBasePos3 = basePos[1];
            leadBasePosLong2 = basePos[0];

        }

        private void findLead(TextReader reader, int startTime, int[] leadPos, Lead[] leads)
        {
            int count = leads.Count();
            for (int i = 0; i < count; i++)
            {
                while (reader.Peek() > 0)
                {
                    string s = reader.ReadLine();
                    Regex rx = new Regex("^" + startTime.ToString() + @" (\d+) m$");
                    Match m = rx.Match(s);
                    if (m.Success)
                    {

                        int v = Convert.ToInt32(m.Groups[1].ToString());
                        leads[i].baseTime = (startTime - timeStartI_II_III) * 0.0004;
                        leads[i].ekgElements.Add(new Lead.Element() { time = 0.0, voltage = (v - leadPos[i]) * 0.001 });

                        // start to collect data
                        while (reader.Peek() > 0)
                        {
                            string s2 = reader.ReadLine();
                            Regex rx2 = new Regex(@"^(\d+) (\d+) l$");
                            Match m2 = rx2.Match(s2);
                            if (!m2.Success)
                                break;

                            int t2 = Convert.ToInt32(m2.Groups[1].ToString());
                            int v2 = Convert.ToInt32(m2.Groups[2].ToString());
                            leads[i].ekgElements.Add(new Lead.Element() { time = (t2 - startTime) * 0.0004, voltage = (v2 - leadPos[i]) * 0.001 });
                        }
                        break;

                    }
                }
            }
        }

        private int findHr(string txt)
        {
            Regex rx = new Regex(@"(?i)Td\s*\((\d+)\).*?\n.*?Vent\. rate|Td\s*\(Vent\. rate.*?\n.*?\((\d+)\)");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                if (!string.IsNullOrEmpty(m.Groups[1].ToString()))
                    return Convert.ToInt32(m.Groups[1].ToString());
                else
                    return Convert.ToInt32(m.Groups[2].ToString());
            }

            return -10000;      // *** -10000 means 'not found'
        }

        private int findPr(string txt)
        {
            Regex rx = new Regex(@"(?i)Td\s*\((\d+|\*)\).*?\n.*?PR interval|Td\s*\(PR interval.*?\n.*?\(ms\).*?\n.*?\((\d+|\*)\)|Td\s*\(PR interval.*?\n.*?\((\d+|\*)\)");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                int Pr = 10000;      // *** 10000 means *                

                try
                {
                    if (!string.IsNullOrEmpty(m.Groups[1].ToString()))
                        Pr = Convert.ToInt32(m.Groups[1].ToString());
                    else if (!string.IsNullOrEmpty(m.Groups[2].ToString()))
                        Pr = Convert.ToInt32(m.Groups[2].ToString());
                    else
                        Pr = Convert.ToInt32(m.Groups[3].ToString());
                }
                catch
                { }

                return Pr;
            }

            return -10000;      // *** -10000 means 'not found'
        }

        private int findQrs(string txt)
        {
            Regex rx = new Regex(@"(?i)Td\s*\((\d+)\).*?\n.*?QRS|Td\s*\(QRS.*?\n.*?\((\d+)\)");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                if (!string.IsNullOrEmpty(m.Groups[1].ToString()))
                    return Convert.ToInt32(m.Groups[1].ToString());
                else
                    return Convert.ToInt32(m.Groups[2].ToString());
            }

            return -10000;      // *** -10000 means 'not found'
        }

        private (int, int) findQt(string txt)
        {
            Regex rx = new Regex(@"(?i)QT/QTc.*?\n.*?\((\d+)/(\d+)\)");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                int Qt = Convert.ToInt32(m.Groups[1].ToString());
                int Qtc = Convert.ToInt32(m.Groups[2].ToString());
                return (Qt, Qtc);
            }

            return (-10000, -10000);  // *** -10000 means 'not found'
        }

        private (int, int, int) findPRT(string txt)
        {
            Regex rx = new Regex(@"(?i)Td\s*\(([-]?\d+|\*)\).*?\n.*?Td\s*\(([-]?\d+|\*)\).*?\n.*?Td\s*\(([-]?\d+|\*)\).*?\n.*?P-R-T");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                int T = 10000;      // *** 10000 means *   
                int R = 10000;      // *** 10000 means *   
                int P = 10000;      // *** 10000 means *   

                try
                {
                    T = Convert.ToInt32(m.Groups[1].ToString());
                }
                catch
                { }
                try
                {
                    R = Convert.ToInt32(m.Groups[2].ToString());
                }
                catch
                { }
                try
                {
                    P = Convert.ToInt32(m.Groups[3].ToString());
                }
                catch
                { }
                return (P, R, T);
            }

            return (-10000, -10000, -10000);  // *** -10000 means 'not found'
        }

        private List<string> findReport(string txt)
        {
            List<string> reports = new List<string>();

            Regex rx = new Regex("(?si)/TP (?:500|400)(.*?)/TP");
            Match m = rx.Match(txt);
            if (m.Success)
            {
                string txt2 = m.Groups[1].ToString();
                Regex rx2 = new Regex(@"(?i)Td\s*\((.{1,})\)");
                MatchCollection mc = rx2.Matches(txt2);
                foreach(Match m2 in mc)
                {
                    if (m2.Success)
                    {
                        reports.Add(m2.Groups[1].ToString());
                    }
                }
            }
            return reports;
        }
    }
}

