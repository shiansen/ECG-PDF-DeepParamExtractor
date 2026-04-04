using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ECGPdfExtractor
{
    class Lead
    {
        public double baseTime;
        public List<Element> ekgElements = new List<Element>();

        public class Element
        {
            public double time;
            public double voltage;
        }
    }
}
