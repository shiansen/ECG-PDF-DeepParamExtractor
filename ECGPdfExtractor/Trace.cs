/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	Trace
//	Trace errors.
//
//	Granotech Limited
//	Author: Uzi Granot
//	Version: 1.0
//	Date: September 1, 2012
//	Copyright (C) 2012 Granotech Limited. All Rights Reserved
//
//	PdfFileAnalyzer application is a free software.
//	It is distributed under the Code Project Open License (CPOL).
//	The document PdfFileAnalyzerReadmeAndLicense.pdf contained within
//	the distribution specify the license agreement and other
//	conditions and notes. You must read this document and agree
//	with the conditions specified in order to use this software.
//
//	Version History:
//
//	Version 1.0 2012/09/01
//		Original revision
//	Version 1.1 2013/04/10
//		Allow program to be compiled in regions that define
//		decimal separator to be non period (comma)
//	Version 1.2 2014/03/10
//		Fix a problem related to PDF files with cross reference
//		stream.
//
/////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Text;

namespace PdfFileAnalyzer
{
/////////////////////////////////////////////////////////////////////
// Trace Class
/////////////////////////////////////////////////////////////////////

static public class Trace
	{
	static private String	TraceFileName;		// trace file name
	static private Int32	MaxAllowedFileSize = 0x10000;

	/////////////////////////////////////////////////////////////////////
	// Open trace file
	/////////////////////////////////////////////////////////////////////

	static public void Open
			(
			String	FileName
			)
		{
		// save full file name
		TraceFileName = Path.GetFullPath(FileName);
		Trace.Write("----");
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// write to trace file
	/////////////////////////////////////////////////////////////////////

	static public void Write
			(
			String Message
			)
		{
		// test file length
		TestSize();

		// open existing or create new trace file
		StreamWriter TraceFile = new StreamWriter(TraceFileName, true);

		// write date and time
		TraceFile.Write(String.Format("{0:yyyy}/{0:MM}/{0:dd} {0:HH}:{0:mm}:{0:ss} ", DateTime.Now));

		// write message
		TraceFile.WriteLine(Message);

		// close the file
		TraceFile.Close();

		// exit
		return;
		}

	/////////////////////////////////////////////////////////////////////
	// Test file size
	// If file is too big, remove first quarter of the file
	/////////////////////////////////////////////////////////////////////

	static private void TestSize()
		{
		// get trace file info
		FileInfo TraceFileInfo = new FileInfo(TraceFileName);

		// if file does not exist or file length less than max allowed file size do nothing
		if(TraceFileInfo.Exists == false || TraceFileInfo.Length <= MaxAllowedFileSize) return;

		// create file info class
		FileStream TraceFile = new FileStream(TraceFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		// seek to 25% length
		TraceFile.Seek(TraceFile.Length / 4, SeekOrigin.Begin);

		// new file length
		Int32 NewFileLength = (Int32) (TraceFile.Length - TraceFile.Position);

		// new file buffer
		Byte[] Buffer = new Byte[NewFileLength];

		// read file to the end
		TraceFile.Read(Buffer, 0, NewFileLength);

		// search for first end of line
		Int32 StartPtr = 0;
		while(StartPtr < 1024 && Buffer[StartPtr++] != '\n');
		if(StartPtr == 1024) StartPtr = 0;

		// seek to start of file
		TraceFile.Seek(0, SeekOrigin.Begin);

		// write 75% top part of file over the start of the file
		TraceFile.Write(Buffer, StartPtr, NewFileLength - StartPtr);

		// truncate the file
		TraceFile.SetLength(TraceFile.Position);

		// close the file
		TraceFile.Close();

		// exit
		return;
		}
	}
}