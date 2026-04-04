/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	ExceptionReport
//	Class designed to produce a meaningful error message from
//	the dot net Exception class. The ReadPdfFile method of the
//	PdfDocument class encloses the analysis process by a try block.
//	If the code generates an exception the catch clause will produce
//	an Exception object. The ExceptionReport class translates the
//	information in this object to an error message including the
//	source module and line number that generated the throw command.
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

namespace PdfFileAnalyzer
{
public static class ExceptionReport
	{
	/////////////////////////////////////////////////////////////////////
	// Get exception message and exception stack
	/////////////////////////////////////////////////////////////////////

	public static String[] GetMessageAndStack
			(
			Object			Sender,
			Exception		Ex
			)
		{
		// get system stack at the time of exception
		String StackTraceStr = Ex.StackTrace;

		// break it into individual lines
		String[] StackTraceLines = StackTraceStr.Split(new Char[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);

		// count all lines containing the name space of this program
		Int32 Count = 0;
		foreach(String Line in StackTraceLines) if(Line.Contains(Sender.GetType().Namespace)) Count++;

		// create a new array of trace lines
		String[] StackTrace = new String[Count + 1];

		// exception error message
		StackTrace[0] = Ex.Message;
		Trace.Write(Ex.Message);

		// add trace lines
		Int32 Index = 0;
		foreach(String Line in StackTraceLines) if(Line.Contains(Sender.GetType().Namespace))
			{
			StackTrace[++Index] = Line;
			Trace.Write(Line);
			}

		// error exit
		return(StackTrace);
		}
	}
}
