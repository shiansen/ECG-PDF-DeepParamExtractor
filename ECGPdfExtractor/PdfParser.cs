/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	PdfParser, PdfFileParser and PdfByteArrayParser
//	The PdfParser is base class designed to parse the PDF file.
//	The PdfFileParser is a derived class to parse the contents of
//	a disk file. The PdfByteArrayParser is a derived class to
//	parse the contents of a byte array.
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
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;

namespace PdfFileAnalyzer
{
// page contents operators
public enum Operator
	{
	FillStrokeNonZeroRule,		// B		Fill and stroke path using nonzero winding number rule			4.10	230
	FillStrokeEvenOddRule,		// B*		Fill and stroke path using even-odd rule						4.10	230
	CloseFillStrokeNonZeroRule,	// b		Close, fill, and stroke path using nonzero winding number rule	4.10	230
	CloseFillStrokeEvenOddRule,	// b*		Close, fill, and stroke path using even-odd rule				4.10	230
	BeginMarkedContentPropList,	// BDC		Begin marked-content sequence with property list				10.7	851
//	BeginInlineImage,			// BI		Begin inline image object										4.42	352
	BeginMarkedContent,			// BMC		Begin marked-content sequence									10.7	851
	BeginText,					// BT		Begin text object												5.4		405
	BeginCompatibility,			// BX		Begin compatibility section										3.29	152
	Bezier,						// c		Append Bezier segment to path (three control points)			4.9		226
	TransMatrix,				// cm		Concatenate matrix to current transformation matrix				4.7		219
	ColorSpaceForStroking,		// CS		Set color space for stroking operations							4.24	287
	ColorSpaceForNonStroking,	// cs		Set color space for nonstroking operations						4.24	287
	LineDashPattern,			// d		Set line dash pattern											4.7		219
	GlyphWidthType3,			// d0		Set glyph width in Type 3 font									5.10	423
	GlyphWidthBBoxType3,		// d1		Set glyph width and bounding box in Type 3 font					5.10	423
	XObject,					// Do		Invoke named XObject											4.37	332
	DefineMarkedContentPropList, // DP		Define marked-content point with property list					10.7	851
//	EndInlineImage,				// EI		End inline image object											4.42	352
	EndMarkedContent,			// EMC		End marked-content sequence										10.7	851
	EndTextObject,				// ET		End text object													5.4		405
	EndCompatibility,			// EX		End compatibility section										3.29	152
	FillNonZeroRule,			// f		Fill path using nonzero winding number rule						4.10	230
	FillEvenOddRule,			// f*		Fill path using even-odd rule									4.10	230
	GrayLevelForStroking,		// G		Set gray level for stroking operations							4.24	288
	GrayLevelForNonStroking,	// g		Set gray level for nonstroking operations						4.24	288
	ParamFromGraphicsStateDict,	// gs		Set parameters from graphics state parameter dictionary			4.7		219
	ClosePath,					// h		Close subpath													4.9		227
	FlatnessTolerance,			// i		Set flatness tolerance											4.7		219
//	BeginInlineImageData,		// ID		Begin inline image data											4.42	352
	LineJoinStyle,				// j		Set line join style												4.7		219
	LineCapStyle,				// J		Set line cap style												4.7		219
	CmykColorForStroking,		// K		Set CMYK color for stroking operations							4.24	288
	CmykColorForNonStroking,	// k		Set CMYK color for nonstroking operations						4.24	288
	LineTo,						// l		Append straight line segment to path							4.9		226
	MoveTo,						// m		Begin new subpath												4.9		226
	MiterLimit,					// M		Set miter limit													4.7		219
	DefineMarkedContent,		// MP		Define marked-content point										10.7	851
	NoPaint,					// n		End path without filling or stroking							4.10	230
	SaveGraphicsState,			// q		Save graphics state												4.7		219
	RestoreGraphicsState,		// Q		Restore graphics state											4.7		219
	Rectangle,					// re		Append rectangle to path										4.9		227
	RgbColorForStroking,		// RG		Set RGB color for stroking operations							4.24	288
	RgbColorForNonStroking,		// rg		Set RGB color for nonstroking operations						4.24	288
	ColorRenderingIntent,		// ri		Set color rendering intent										4.7		219
	Stroke,						// S		Stroke path														4.10	230
	CloseStroke,				// s		Close and stroke path											4.10	230
	ColorForStroking,			// SC		Set color for stroking operations								4.24	287
	ColorForNonStroking,		// sc		Set color for nonstroking operations							4.24	288
	ColorForStrokingSpecial,	// SCN		Set color for stroking operations (ICCBased & special color)	4.24	288
	ColorForNonStrokingSpecial,	// scn		Set color for nonstroking operations (ICCBased & special color)	4.24	288
	PaintAreaShadingPattern,	// sh		Paint area defined by shading pattern							4.27	303
	MoveToStartOfNextLine,		// T*		Move to start of next text line									5.5		406
	SetCharSpacing,				// Tc		Set character spacing											5.2		398
	MoveTextPos,				// Td		Move text position												5.5		406
	MoveTextPosSetLeading,		// TD		Move text position and set leading								5.5		406
	SelectFontAndSize,			// Tf		Set text font and size											5.2		398
	ShowText,					// Tj		Show text														5.6		407
	ShowTextWithGlyphPos,		// TJ		Show text, allowing individual glyph positioning				5.6		408
	TextLeading,				// TL		Set text leading												5.2		398
	TextMatrix,					// Tm		Set text matrix and text line matrix							5.5		406
	TextRenderingMode,			// Tr		Set text rendering mode											5.2		398
	TextRize,					// Ts		Set text rise													5.2		398
	TextWorkSpacing,			// Tw		Set word spacing												5.2		398
	TextHorizontalScaling,		// Tz		Set horizontal text scaling										5.2		398
	BezierNoP1,					// v		Append Bezier segment to path (initial point replicated)		4.9		226
	ClippingPathNonZeroRule,	// W		Set clipping path using nonzero winding number rule				4.11	235
	ClippingPathEvenOddRule,	// W*		Set clipping path using even-odd rule							4.11	235
	LineWidth,					// w		Set line width													4.7		219
	BezierNoP2,					// y		Append bEZIER segment to path (final point replicated)			4.9		226
	MoveToNextLineAndShow,		// '		Move to next line and show text									5.6		407
	WordCharSpacingShowText,	// "		Set word and character spacing, move to next line & show text	5.6		407
	Count,
	}

////////////////////////////////////////////////////////////////////
// operator control table
////////////////////////////////////////////////////////////////////

public class OpCtrl : IComparable<OpCtrl>
	{
	public String		OpStr;
	public Operator		OpCode;

	public OpCtrl
			(
			String		OpStr,
			Operator	OpCode
			)
		{
		this.OpStr = OpStr;
		this.OpCode = OpCode;
		return;
		}

	public OpCtrl
			(
			String OpStr
			)
		{
		this.OpStr = OpStr;
		return;
		}

	public Int32 CompareTo
			(
			OpCtrl	Other
			)
		{
		return(String.Compare(this.OpStr, Other.OpStr));
		}
	}

/////////////////////////////////////////////////////////////////////
// Number Format Info
// Adobe readers expect decimal separator to be period.
// Some countries define decimal separator as comma.
// The project uses NFI.DecSep to force period for all regions.
/////////////////////////////////////////////////////////////////////

public static class NFI
	{
	internal static NumberFormatInfo DecSep;			// number format info decimal separetor is period
	static NFI()
		{
		// number format (decimal separator is period)
		DecSep = new NumberFormatInfo();
		DecSep.NumberDecimalSeparator = ".";
		return;
		}
	}

////////////////////////////////////////////////////////////////////
// Parse PDF file
////////////////////////////////////////////////////////////////////

public class PdfFileParser : PdfParser
	{
	private BinaryReader PdfFile;

	////////////////////////////////////////////////////////////////////
	// constructor
	////////////////////////////////////////////////////////////////////

	public PdfFileParser
			(
			BinaryReader PdfFile
			) : base(false)
		{
		this.PdfFile = PdfFile;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Read character
	////////////////////////////////////////////////////////////////////
	
	public override Char ReadChar()
		{
		try
			{
			return((Char) PdfFile.ReadByte());
			}
		catch
			{
			throw new ApplicationException("Unexpected end of file");
			}
		}

	////////////////////////////////////////////////////////////////////
	// Step back
	////////////////////////////////////////////////////////////////////
	
	public override void StepBack()
		{
		PdfFile.BaseStream.Position--;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Get position
	////////////////////////////////////////////////////////////////////
	
	public override Int32 GetPos()
		{
		return((Int32) PdfFile.BaseStream.Position);
		}

	////////////////////////////////////////////////////////////////////
	// Set position
	////////////////////////////////////////////////////////////////////
	
	public override void SetPos(Int32 Pos)
		{
		PdfFile.BaseStream.Position = Pos;
		return;
		}
	}

////////////////////////////////////////////////////////////////////
// Parse PDF contents stream
////////////////////////////////////////////////////////////////////

public class PdfByteArrayParser : PdfParser
	{
	private Byte[]	Contents;
	private Int32	Position;

	////////////////////////////////////////////////////////////////////
	// constructor
	////////////////////////////////////////////////////////////////////

	public PdfByteArrayParser
			(
			Byte[]	Contents,
			Boolean	StreamMode
			) : base(StreamMode)
		{
		this.Contents = Contents;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Read character
	////////////////////////////////////////////////////////////////////
	
	public override Char ReadChar()
		{
		return(Position == Contents.Length ? PdfParser.EOF : (Char) Contents[Position++]);
		}

	////////////////////////////////////////////////////////////////////
	// Step back
	////////////////////////////////////////////////////////////////////
	
	public override void StepBack()
		{
		Position--;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Get position
	////////////////////////////////////////////////////////////////////
	
	public override Int32 GetPos()
		{
		return(Position);
		}

	////////////////////////////////////////////////////////////////////
	// Set position
	////////////////////////////////////////////////////////////////////
	
	public override void SetPos(Int32 Pos)
		{
		Position = Pos;
		return;
		}
	}

////////////////////////////////////////////////////////////////////
// Parse PDF file base class
////////////////////////////////////////////////////////////////////

public class PdfParser
	{
	private Boolean		StreamMode;
	private Char		NextChar;

	protected const Char EOF = (Char) 0xffff;

	// page contents operators array
	// this array is sorted by the operator code string order
	// during program initialization in Parse class static constructor
	private static OpCtrl[] OpCtrlArray = new OpCtrl[]
		{
		new OpCtrl("b", Operator.CloseFillStrokeNonZeroRule),
		new OpCtrl("B", Operator.FillStrokeNonZeroRule),
		new OpCtrl("b*", Operator.CloseFillStrokeEvenOddRule),
		new OpCtrl("B*", Operator.FillStrokeEvenOddRule),
		new OpCtrl("BDC", Operator.BeginMarkedContentPropList),
//		new OpCtrl("BI", Operator.BeginInlineImage),
		new OpCtrl("BMC", Operator.BeginMarkedContent),
		new OpCtrl("BT", Operator.BeginText),
		new OpCtrl("BX", Operator.BeginCompatibility),
		new OpCtrl("c", Operator.Bezier),
		new OpCtrl("cm", Operator.TransMatrix),
		new OpCtrl("CS", Operator.ColorSpaceForStroking),
		new OpCtrl("cs", Operator.ColorSpaceForNonStroking),
		new OpCtrl("d", Operator.LineDashPattern),
		new OpCtrl("d0", Operator.GlyphWidthType3),
		new OpCtrl("d1", Operator.GlyphWidthBBoxType3),
		new OpCtrl("Do", Operator.XObject),
		new OpCtrl("DP", Operator.DefineMarkedContentPropList),
//		new OpCtrl("EI", Operator.EndInlineImage),
		new OpCtrl("EMC", Operator.EndMarkedContent),
		new OpCtrl("ET", Operator.EndTextObject),
		new OpCtrl("EX", Operator.EndCompatibility),
		new OpCtrl("f", Operator.FillNonZeroRule),
		new OpCtrl("F", Operator.FillNonZeroRule),
		new OpCtrl("f*", Operator.FillEvenOddRule),
		new OpCtrl("G", Operator.GrayLevelForStroking),
		new OpCtrl("g", Operator.GrayLevelForNonStroking),
		new OpCtrl("gs", Operator.ParamFromGraphicsStateDict),
		new OpCtrl("h", Operator.ClosePath),
		new OpCtrl("i", Operator.FlatnessTolerance),
//		new OpCtrl("ID", Operator.BeginInlineImageData),
		new OpCtrl("j", Operator.LineJoinStyle),
		new OpCtrl("J", Operator.LineCapStyle),
		new OpCtrl("K", Operator.CmykColorForStroking),
		new OpCtrl("k", Operator.CmykColorForNonStroking),
		new OpCtrl("l", Operator.LineTo),
		new OpCtrl("m", Operator.MoveTo),
		new OpCtrl("M", Operator.MiterLimit),
		new OpCtrl("MP", Operator.DefineMarkedContent),
		new OpCtrl("n", Operator.NoPaint),
		new OpCtrl("q", Operator.SaveGraphicsState),
		new OpCtrl("Q", Operator.RestoreGraphicsState),
		new OpCtrl("re", Operator.Rectangle),
		new OpCtrl("RG", Operator.RgbColorForStroking),
		new OpCtrl("rg", Operator.RgbColorForNonStroking),
		new OpCtrl("ri", Operator.ColorRenderingIntent),
		new OpCtrl("s", Operator.CloseStroke),
		new OpCtrl("S", Operator.Stroke),
		new OpCtrl("SC", Operator.ColorForStroking),
		new OpCtrl("sc", Operator.ColorForNonStroking),
		new OpCtrl("SCN", Operator.ColorForStrokingSpecial),
		new OpCtrl("scn", Operator.ColorForNonStrokingSpecial),
		new OpCtrl("sh", Operator.PaintAreaShadingPattern),
		new OpCtrl("T*", Operator.MoveToStartOfNextLine),
		new OpCtrl("Tc", Operator.SetCharSpacing),
		new OpCtrl("Td", Operator.MoveTextPos),
		new OpCtrl("TD", Operator.MoveTextPosSetLeading),
		new OpCtrl("Tf", Operator.SelectFontAndSize),
		new OpCtrl("Tj", Operator.ShowText),
		new OpCtrl("TJ", Operator.ShowTextWithGlyphPos),
		new OpCtrl("TL", Operator.TextLeading),
		new OpCtrl("Tm", Operator.TextMatrix),
		new OpCtrl("Tr", Operator.TextRenderingMode),
		new OpCtrl("Ts", Operator.TextRize),
		new OpCtrl("Tw", Operator.TextWorkSpacing),
		new OpCtrl("Tz", Operator.TextHorizontalScaling),
		new OpCtrl("v", Operator.BezierNoP1),
		new OpCtrl("w", Operator.LineWidth),
		new OpCtrl("W", Operator.ClippingPathNonZeroRule),
		new OpCtrl("W*", Operator.ClippingPathEvenOddRule),
		new OpCtrl("y", Operator.BezierNoP2),
		new OpCtrl("'", Operator.MoveToNextLineAndShow),
		new OpCtrl("\"", Operator.WordCharSpacingShowText),
		};

	// array of strings of page contents operator
	// this array is created during program initialization in Parse class static constructor.
	// the string are sorted by the Operator enumeration value.
	// the array allows a direct translation from Operator code to string value
	private static String[] OpStr = new String[(Int32) Operator.Count];
	public static String OperatorCode
			(
			Operator Op
			)
		{
		return(OpStr[(Int32) Op]);
		}

	////////////////////////////////////////////////////////////////////
	// static constructor
	////////////////////////////////////////////////////////////////////

	static PdfParser()
		{
		// sort the operator control array
		Array.Sort(OpCtrlArray);

		// create the operator string array
		foreach(OpCtrl Op in OpCtrlArray) if(Op.OpStr != "F") OpStr[(Int32) Op.OpCode] = Op.OpStr;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Delimiter and white space test routines
	////////////////////////////////////////////////////////////////////

	// translation table for IsDelimiter and IsWhiteSpace methods
	// white space is: null, tab, line feed, form feed, carriage return and space
	// delimiter is: white space, (, ), <, >, [, ], {, }, /, and %
	private static Byte[] Delimiter = new Byte[256] 
			{
			//          0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
			/* 000 */	3, 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 0, 3, 3, 0, 0, 
			/* 016 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 032 */	3, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 
			/* 048 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 
			/* 064 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 080 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 
			/* 096 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 112 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 
			/* 128 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 144 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 160 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 176 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 192 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  
			/* 208 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
			/* 224 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  
			/* 240 */	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
			};

	public static Boolean IsDelimiter(Char Ch) {return(Delimiter[(Int32) Ch] != 0);}
	public static Boolean IsDelimiter(Byte Ch) {return(Delimiter[(Int32) Ch] != 0);}
	public static Boolean IsDelimiter(Int32 Ch) {return(Delimiter[Ch] != 0);}
	public static Boolean IsWhiteSpace(Char Ch) {return((Delimiter[(Int32) Ch] & 2) != 0);}
	public static Boolean IsWhiteSpace(Byte Ch) {return((Delimiter[(Int32) Ch] & 2) != 0);}
	public static Boolean IsWhiteSpace(Int32 Ch) {return((Delimiter[Ch] & 2) != 0);}

	////////////////////////////////////////////////////////////////////
	// Constructor
	////////////////////////////////////////////////////////////////////

	public PdfParser
			(
			Boolean		StreamMode
			)
		{
		this.StreamMode = StreamMode;
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Read first character
	////////////////////////////////////////////////////////////////////
	
	public void ReadFirstChar()
		{
		NextChar = ReadChar();
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Parse next item
	////////////////////////////////////////////////////////////////////
	
	public PdfBase ParseNextItem()
		{
		// loop in case of one or more comments
		for(;;)
			{
			// skip white space
			SkipWhiteSpace();

			// not a comment
			if(NextChar != '%') break;

			// read characters until next end of line
			for(;;)
				{
				NextChar = ReadChar();
				if(NextChar == EOF) return(PdfBase.Empty);
				if(NextChar == '\n' || NextChar == '\r') break;
				}
			}

		// end of file
		if(NextChar == EOF) return(PdfBase.Empty);

		// string
		if(NextChar == '(') return(ParseString());

		// array
		if(NextChar == '[') return(ParseArray());

		// hex string or dictionary
		if(NextChar == '<')
			{
			// test for dictionary
			if(ReadChar() == '<') return(ParseDictionary());

			// move pointer back
			StepBack();

			// hex string
			return(ParseHexString());
			}

		// next content element
		StringBuilder NextItem = new StringBuilder();
		NextItem.Append(NextChar);

		// add more characters until next delimiter
		while((NextChar = ReadChar()) != EOF && !IsDelimiter(NextChar)) NextItem.Append(NextChar);

		// name
		if(NextItem[0] == '/')
			{
			// empty name
			if(NextItem.Length == 1) throw new ApplicationException("Empty name token");

			// exit
			return(new PdfName(NextItem.ToString()));
			}

		// integer
		Int32 IntVal;
		if(Int32.TryParse(NextItem.ToString(), out IntVal))
			{
			// test for reference or object
			if(!StreamMode && IntVal > 0)
				{
				// we are looking for n 0 R or n 0 obj
				switch(TestReference())
					{
					case 'R':
						return(new PdfRef(IntVal));

					case 'o':
						return(new PdfIndirectObject(IntVal));
					}
				}
			return(new PdfInt(IntVal));
			}

		// real number with period as decimal separator regardless of region
		Single RealVal;
		if(Single.TryParse(NextItem.ToString(),
			NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, NFI.DecSep, out RealVal)) return(new PdfReal(RealVal));

		// false
		if(NextItem.ToString() == "false") return(new PdfBoolean(false));

		// true
		if(NextItem.ToString() == "true") return(new PdfBoolean(true));

		// null
		if(NextItem.ToString() == "null") return(new PdfNull());

		// parse page contents
		if(StreamMode)
			{
			// begin inline image
			if(NextItem.ToString() == "BI") return(ParseInlineImage());

			// search for contents operator
			Int32 OpIndex = Array.BinarySearch(OpCtrlArray, new OpCtrl(NextItem.ToString()));

			// not found
			if(OpIndex < 0) throw new ApplicationException("Parsing failed: Unknown contents operator");

			// PDF operator object
			return(new PdfOp(OpCtrlArray[OpIndex].OpCode));
			}

		// stream special case
		if(NextItem.ToString() == "stream")
			{
			// stream must be foloowed by NL or CR and NL
			if(NextChar == '\n' || NextChar == '\r' && ReadChar() == '\n') return(new PdfKeyword(KeyWord.Stream));

			// error
			throw new ApplicationException("Stream word must be followed by EOL");
			}

		// endstream
		if(NextItem.ToString() == "endstream") return(new PdfKeyword(KeyWord.EndStream));

		// endobj
		if(NextItem.ToString() == "endobj") return(new PdfKeyword(KeyWord.EndObj));

		// xref
		if(NextItem.ToString() == "xref") return(new PdfKeyword(KeyWord.Xref));

		// xref n
		if(NextItem.ToString() == "n") return(new PdfKeyword(KeyWord.N));

		// xref f
		if(NextItem.ToString() == "f") return(new PdfKeyword(KeyWord.F));

		// trailer
		if(NextItem.ToString() == "trailer") return(new PdfKeyword(KeyWord.Trailer));

		// error
		throw new ApplicationException("Parsing failed: Unknown token");
		}

	////////////////////////////////////////////////////////////////////
	// Skip white space
	////////////////////////////////////////////////////////////////////
	
	public void SkipWhiteSpace()
		{
		// skip white space
		if(IsWhiteSpace(NextChar)) while((NextChar = ReadChar()) != EOF && IsWhiteSpace(NextChar));
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Test for reference
	////////////////////////////////////////////////////////////////////
	
	private Int32 TestReference()
		{
		// save current file position
		Int32 Pos = GetPos();

		// next character
		Char TempChar = NextChar;

		// next character must be space
		if(!IsWhiteSpace(TempChar)) goto not_found;

		// skip additional white space
		while((TempChar = ReadChar()) != EOF && IsWhiteSpace(TempChar));

		// next character must be zero
		if(TempChar != '0') goto not_found;

		// next character must be white space
		TempChar = ReadChar();
		if(!IsWhiteSpace(TempChar)) goto not_found;

		// skip additional white space
		while((TempChar = ReadChar()) != EOF && IsWhiteSpace(TempChar));

		// next character must be R or obj
		if(TempChar != 'R' && (TempChar != 'o' || ReadChar() != 'b' || ReadChar() != 'j')) goto not_found;

		// save result
		Int32 Result = TempChar;

		// next character must be a delimiter
		TempChar = ReadChar();
		if(!IsDelimiter(TempChar)) goto not_found;

		// result is 'R' for reference and 'o' for object
		NextChar = TempChar;
		return(Result);

		// not found
		not_found:

		// restore position
		SetPos(Pos);
		return(0);
		}

	////////////////////////////////////////////////////////////////////
	// Read string value
	////////////////////////////////////////////////////////////////////
	
	private PdfBase ParseString()
		{
		// create value string
		StringBuilder StrItem = new StringBuilder("(");

		// parenthesis protection logic
		Boolean Esc = false;
		Int32 Level = 0;

		// read string to the end
		for(;;)
			{
			// read next character
			NextChar = ReadChar();
			if(NextChar == EOF) throw new ApplicationException("Invalid string (End of contents)");

			// backslash state
			if(Esc)
				{
				// reset backslash escape and accept current character without testing
				Esc = false;
				}

			// not backslash state
			else
				{
				// set escape logic for next character
				if(NextChar == '\\') Esc = true;

				// left parenthesis
				else if(NextChar == '(') Level++;

				// right parenthesis
				else if(NextChar == ')')
					{
					if(Level == 0) break;
					Level--;
					}
				}

			// append it in value
			StrItem.Append(NextChar);
			}

		// append terminating value
		StrItem.Append(')');

		// read next character after closing )
		NextChar = ReadChar();

		// exit
		return(new PdfStr(StrItem.ToString()));
		}

	////////////////////////////////////////////////////////////////////
	// Parse hex string item
	////////////////////////////////////////////////////////////////////

	private PdfBase ParseHexString()
		{
		// create value string
		StringBuilder HexStr = new StringBuilder("<");

		// add more hexadecimal numbers until next closing >
		for(;;)
			{
			// read next character
			NextChar = ReadChar();
			if(NextChar == EOF) throw new ApplicationException("Invalid hex string (End of contents)");

			// end of string
			if(NextChar == '>') break;

			// ignore white space within the string
			if(IsWhiteSpace(NextChar)) continue;

			// test for hex digits
			if(NextChar < '0' || NextChar > '9' && NextChar < 'A' || NextChar > 'F' && NextChar < 'a' || NextChar > 'f')
				throw new ApplicationException("Invalid hex string");

			// append to hex array
			HexStr.Append(NextChar);
			}

		// append terminating value
		HexStr.Append('>');

		// read next character after closing >
		NextChar = ReadChar();

		// exit
		return(new PdfHex(HexStr.ToString()));
		}

	////////////////////////////////////////////////////////////////////
	// Parse Array
	////////////////////////////////////////////////////////////////////
	
	private PdfArray ParseArray()
		{
		// create empty array
		List<PdfBase> ResultArray = new List<PdfBase>();

		// read first character after [
		NextChar = ReadChar();

		// loop until closing ] or EOF
		for(;;)
			{
			// skip white space
			SkipWhiteSpace();

			// end of file
			if(NextChar == EOF) throw new ApplicationException("Invalid array (end of contents)");

			// end of array
			if(NextChar == ']') break;

			// parse next item
			PdfBase NextItem = ParseNextItem();

			// end of file
			if(NextItem.IsEmpty) throw new ApplicationException("Invalid array (end of contents)");

			// add to result array
			ResultArray.Add(NextItem);
			}

		// read next character after closing ]
		NextChar = ReadChar();

		// exit
		return(new PdfArray(ResultArray.ToArray()));			
		}

	////////////////////////////////////////////////////////////////////
	// Parse Dictionary
	////////////////////////////////////////////////////////////////////
	
	private PdfBase ParseDictionary()
		{
		// create empty dictionary
		List<PdfPair> ResultDict = new List<PdfPair>();

		// read first character after <<
		NextChar = ReadChar();

		// loop until closing >> or EOF
		for(;;)
			{
			// skip white space
			SkipWhiteSpace();

			// end of file
			if(NextChar == EOF) throw new ApplicationException("Invalid dictionary (end of contents)");

			// end of array
			if(NextChar == '>') break;

			// next character must be / for name
			if(NextChar != '/') throw new ApplicationException("Invalid dictionary (name entry must have /)");
			
			// read name
			StringBuilder Name = new StringBuilder();
			Name.Append(NextChar);

			// add more characters until next delimiter
			while((NextChar = ReadChar()) != EOF && !IsDelimiter(NextChar)) Name.Append(NextChar);

			// read next item
			PdfBase Value = ParseNextItem();

			// end of file
			if(Value.IsEmpty) throw new ApplicationException("Invalid dictionary (end of contents)");

			// create pair
			PdfPair Pair = new PdfPair(Name.ToString(), Value);

			// keep dictionary sorted
			Int32 Index = ResultDict.BinarySearch(Pair);
			if(Index >= 0) throw new ApplicationException("Invalid dictionary (duplicate keys)");

			// add to result dictionary
			ResultDict.Insert(~Index, Pair);
			}

		// read next character after first >
		NextChar = ReadChar();

		// must be a second >
		if(NextChar == EOF || NextChar != '>') throw new ApplicationException("Invalid dictionary (missing terminating >>");

		// read next character after second >
		NextChar = ReadChar();

		// exit
		return(new PdfDict(ResultDict.ToArray()));			
		}

	////////////////////////////////////////////////////////////////////
	// Parse inline image
	////////////////////////////////////////////////////////////////////
	
	private PdfBase ParseInlineImage()
		{
		// create empty dictionary
		List<PdfPair> ResultDict = new List<PdfPair>();

		// loop until DI
		for(;;)
			{
			// skip white space
			SkipWhiteSpace();

			// end of file
			if(NextChar == EOF) throw new ApplicationException("Invalid inline image (end of contents)");

			// next character must be / for name
			if(NextChar != '/')
				{
				// end of inline image part
				if(NextChar == 'I' && ReadChar() == 'D') break;

				// error
				throw new ApplicationException("Invalid inline image (name entry must have /)");
				}
			
			// read name
			StringBuilder Name = new StringBuilder();
			Name.Append(NextChar);

			// add more characters until next delimiter
			while((NextChar = ReadChar()) != EOF && !IsDelimiter(NextChar)) Name.Append(NextChar);

			// read next item
			PdfBase Value = ParseNextItem();

			// end of file
			if(Value.IsEmpty) throw new ApplicationException("Invalid inline image (end of contents)");

			// create pair
			PdfPair Pair = new PdfPair(Name.ToString(), Value);

			// keep inline image sorted
			Int32 Index = ResultDict.BinarySearch(Pair);
			if(Index >= 0) throw new ApplicationException("Invalid inline image (duplicate keys)");

			// add to result inline image
			ResultDict.Insert(~Index, Pair);
			}

		// read next character after ID
		NextChar = ReadChar();

		// end of file error
		if(NextChar == EOF) throw new ApplicationException("Invalid inline image (end of contents)");

		// if not white space step back
		if(!PdfParser.IsWhiteSpace(NextChar)) StepBack();

		// accumulate bitmap
		List<Byte> BitMap = new List<Byte>();

		// termination state
		Int32 State = 0;

		// WARNING this method is not totaly reliable.
		// If image will have EI white space it will not work.
		// look for EI white space
		for(;;)
			{
			// read next character
			NextChar = ReadChar();

			// end of file error
			if(NextChar == EOF) throw new ApplicationException("Invalid inline image (end of contents)");

			// save it in bitmap
			BitMap.Add((Byte) NextChar);

			// test for termination
			switch(State)
				{
				case 0:
					if(NextChar == 'E') State++;
					break;

				case 1:
					if(NextChar == 'I') State++;
					else State = 0;
					break;

				case 2:
					if(PdfParser.IsWhiteSpace(NextChar)) State++;
					else State = 0;
					break;
				}

			// we have EI white space
			if(State == 3) break;
			}

		// remove the last three bytes
		BitMap.RemoveRange(BitMap.Count - 3, 3);

		// exit
		return(new PdfInlineImage(new PdfDict(ResultDict.ToArray()), BitMap.ToArray()));			
		}

	////////////////////////////////////////////////////////////////////
	// Read character
	////////////////////////////////////////////////////////////////////
	
	public virtual Char ReadChar()
		{
		return(EOF);
		}

	////////////////////////////////////////////////////////////////////
	// Step back
	////////////////////////////////////////////////////////////////////
	
	public virtual void StepBack()
		{
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Get position
	////////////////////////////////////////////////////////////////////
	
	public virtual Int32 GetPos()
		{
		return(0);
		}

	////////////////////////////////////////////////////////////////////
	// Set position
	////////////////////////////////////////////////////////////////////
	
	public virtual void SetPos(Int32 Pos)
		{
		return;
		}
	}
}
