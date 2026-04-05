using PdfFileAnalyzer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ECGPdfExtractor
{
    public partial class Form1 : Form
    {
        private string appPath;
        public Form1()
        {
            InitializeComponent();
            
            appPath = Path.GetDirectoryName(Application.ExecutablePath);
            if (appPath.Last() != '\\')
                appPath += @"\";

        }

        private void WriteEkgElements(StreamWriter w, string leadName, Lead lead)
        {
            w.Write(leadName + ",");
            w.WriteLine(lead.baseTime);
            for (int i = 0; i < lead.ekgElements.Count; i++)
            {
                w.Write(lead.ekgElements[i].time);
                if (i != lead.ekgElements.Count - 1)
                    w.Write(",");
            }
            w.WriteLine();

            for (int i = 0; i < lead.ekgElements.Count; i++)
            {
                w.Write(lead.ekgElements[i].voltage);
                if (i != lead.ekgElements.Count - 1)
                    w.Write(",");
            }
            w.WriteLine();
        }

        private void Log(string s)
        {
            using (TextWriter w = File.AppendText(appPath + "log.txt"))
            {
                w.WriteLine(s);
            }
        }

        private void btConvert_Click(object sender, EventArgs e)
        {
            tbMsg.Clear();

            string folder = appPath;

            FolderBrowserDialog folderDialog = new FolderBrowserDialog();
            folderDialog.SelectedPath = appPath;
            DialogResult result = folderDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                folder = folderDialog.SelectedPath;
                if (folder.Last() != '\\')
                    folder += "\\";

                string[] filenames = Directory.GetFiles(folder, "*.pdf");
                for (int i = 0; i < filenames.Count(); i++)
                {
                    int x = filenames[i].LastIndexOf('\\');
                    filenames[i] = filenames[i].Substring(x + 1, filenames[i].Length - x - 5);
                }
                // load document
                foreach (var dir in filenames)
                {
                    PdfDocument Document = new PdfDocument();
                    if (Document.ReadPdfFile(folder + dir + ".pdf", folder + dir))
                        Document = null;

                    // process "StreamObj_21.txt", iterate all dirs
                    // sometimes, it's "StreamObj_8.txt" or something similar
                    var ll = Directory.GetFiles(folder + dir).Where(f => f.ToLower().Contains("stream"));
                    var maxSize = ll.Max(f => new FileInfo(f).Length);
                    var file = ll.First(f => new FileInfo(f).Length == maxSize);

                    if (File.Exists(file))
                    {
                        tbMsg.AppendText("Converting " + dir + ".pdf to EKG data file... ");
                        try
                        {
                            EKG ekg = new EKG(file);

                            using (StreamWriter w = File.CreateText(folder + dir + "\\" + dir + "_ekg_data.txt"))
                            {
                                string[] chtnoDate = dir.Split('_');

                                w.WriteLine("Date, " + chtnoDate[2] + chtnoDate[3]);

                                WriteEkgElements(w, "I", ekg.I);
                                WriteEkgElements(w, "II", ekg.II);
                                WriteEkgElements(w, "III", ekg.III);
                                WriteEkgElements(w, "aVR", ekg.aVR);
                                WriteEkgElements(w, "aVL", ekg.aVL);
                                WriteEkgElements(w, "aVF", ekg.aVF);
                                WriteEkgElements(w, "V1", ekg.V1);
                                WriteEkgElements(w, "V2", ekg.V2);
                                WriteEkgElements(w, "V3", ekg.V3);
                                WriteEkgElements(w, "V4", ekg.V4);
                                WriteEkgElements(w, "V5", ekg.V5);
                                WriteEkgElements(w, "V6", ekg.V6);
                                WriteEkgElements(w, "longII", ekg.longII);

                                w.WriteLine("Hr");
                                w.WriteLine(ekg.Hr);

                                w.WriteLine("Pr");
                                w.WriteLine(ekg.Pr);

                                w.WriteLine("Qrs");
                                w.WriteLine(ekg.Qrs);

                                w.WriteLine("Qt");
                                w.WriteLine(ekg.Qt);

                                w.WriteLine("Qtc");
                                w.WriteLine(ekg.Qtc);

                                w.WriteLine("Paxis");
                                w.WriteLine(ekg.Paxis);
                                w.WriteLine("Raxis");
                                w.WriteLine(ekg.Raxis);
                                w.WriteLine("Taxis");
                                w.WriteLine(ekg.Taxis);

                            }
                        }
                        catch (Exception ex)
                        {
                            Log(ex.Message);
                        }

                        if (cbDelete.Checked)
                        {
                            foreach (var f in Directory.GetFiles(folder + dir))
                            {
                                if (f.ToLower().Contains("objectsummary") ||
                                    f.ToLower().Contains("pageobj") ||
                                    f.ToLower().Contains("pagesource") ||
                                    f.ToLower().Contains("stream") ||
                                    f.ToLower().EndsWith(".pdf"))
                                {
                                    File.Delete(f);
                                }

                            }
                        }

                        tbMsg.AppendText("OK\r\n");
                    }
                }


            }
        }
    }
}
