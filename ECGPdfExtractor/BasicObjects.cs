/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	BasicObjects
//	This source code defines the basic objects used by the PDF file.
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
using System.Text;

namespace PdfFileAnalyzer
{
////////////////////////////////////////////////////////////////////
// PDF object base class
////////////////////////////////////////////////////////////////////

public class PdfBase
	{
	// derived class is array
	public Boolean IsArray
		{
		get
			{
			return(GetType() == typeof(PdfArray));
			}
		}

	// derived class is dictionary
	public Boolean IsDictionary
		{
		get
			{
			return(GetType() == typeof(PdfDict));
			}
		}

	// object is end of file or not found
	public Boolean IsEmpty
		{
		get
			{
			return(GetType() == typeof(PdfBase));
			}
		}

	// derived class is inline image
	public Boolean IsInlineImage
		{
		get
			{
			return(GetType() == typeof(PdfInlineImage));
			}
		}

	// derived class is a name
	public Boolean IsName
		{
		get
			{
			return(GetType() == typeof(PdfName));
			}
		}

	// derived class is either an integer or real number
	public Boolean IsNumber
		{
		get
			{
			return(GetType() == typeof(PdfInt) || GetType() == typeof(PdfReal));
			}
		}

	// derived class is page content operator
	public Boolean IsOperator
		{
		get
			{
			return(GetType() == typeof(PdfOp));
			}
		}

	// derived class is object indirect reference
	public Boolean IsReference
		{
		get
			{
			return(GetType() == typeof(PdfRef));
			}
		}

	// derived class is a stream
	public Boolean IsStream
		{
		get
			{
			return(GetType() == typeof(PdfStream));
			}
		}

	// if derived class is an array, return array of objects
	public PdfBase[] ToArray
		{
		get
			{
			return(GetType() == typeof(PdfArray) ? ((PdfArray) this).ArrayValue : null);
			}
		}

	// if derived class is a dictionary, return dictionary object
	public PdfDict ToDictionary
		{
		get
			{
			return(GetType() == typeof(PdfDict) ? (PdfDict) this : null);
			}
		}

	// if derived class is a key word, return the key word
	public KeyWord ToKeyWord
		{
		get
			{
			return(GetType() == typeof(PdfKeyword) ? ((PdfKeyword) this).KeywordValue : KeyWord.Undefined);
			}
		}

	// if derived class is a name, return the name as a string
	public String ToName
		{
		get
			{
			return(GetType() == typeof(PdfName) ? ((PdfName) this).NameValue : null);
			}
		}

	// if derived class is a number, return the number as a Single
	public Single ToNumber
		{
		get
			{
			if(GetType() == typeof(PdfInt)) return(((PdfInt) this).IntValue);
			if(GetType() == typeof(PdfReal)) return(((PdfReal) this).RealValue);
			return(Single.NaN);
			}
		}

	// if derived class is a reference, return the object number as integer
	public Int32 ToObjectRefNo
		{
		get
			{
			return(GetType() == typeof(PdfRef) ? ((PdfRef) this).ObjNoValue : 0);
			}
		}

	// if derived class is an operator, return the operator enumeration
	public Operator ToOperator
		{
		get
			{
			return(GetType() == typeof(PdfOp) ? ((PdfOp) this).OpValue : (Operator) (-1));
			}
		}

	// if derived class is an indirect object, return the object number as an integer
	public Int32 ToObjectNo
		{
		get
			{
			return(GetType() == typeof(PdfIndirectObject) ? ((PdfIndirectObject) this).ObjectNo : 0);
			}
		}

	// if derived class is a stream, return the stream dictionary
	public PdfDict StreamDictionary
		{
		get
			{
			return(GetType() == typeof(PdfStream) ? ((PdfStream) this).Dictionary : null);
			}
		}

	// if derived class is a stream, return the stream contents
	public Byte[] StreamContents
		{
		get
			{
			return(GetType() == typeof(PdfStream) ? ((PdfStream) this).Contents : null);
			}
		set
			{
			((PdfStream) this).Contents = value;
			return;
			}
		}

	// if derived class is a stream, return the stream position in the file
	public Int32 StreamPosition
		{
		get
			{
			return(GetType() == typeof(PdfStream) ? ((PdfStream) this).Position : 0);
			}
		}

	// if derived class is a stream, return the stream length or set the stream length
	public Int32 StreamLength
		{
		get
			{
			return(GetType() == typeof(PdfStream) ? ((PdfStream) this).Length : 0);
			}
		set
			{
			((PdfStream) this).Length = value;
			return;
			}
		}

	// if derived class is an integer return true and set result to value. Otherwise return false and set result to 0
	public Boolean GetInteger
			(
			out Int32 Result
			)
		{
		if(GetType() == typeof(PdfInt))
			{
			Result = ((PdfInt) this).IntValue;
			return(true);
			}
		Result = 0;
		return(false);
		}

	// virtual object type to string
	public virtual String TypeToString()
		{
		return(null);
		}

	// empty PDF base class for end of file and not found
	public static PdfBase Empty = new PdfBase();
	}

////////////////////////////////////////////////////////////////////
// PDF boolean object
////////////////////////////////////////////////////////////////////

public class PdfBoolean : PdfBase
	{
	public Boolean BooleanValue;
	public PdfBoolean(Boolean BooleanValue)
		{
		this.BooleanValue = BooleanValue;
		return;
		}
	public override String ToString()
		{
		return(BooleanValue ? "true" : "false");
		}
	public override String TypeToString()
		{
		return("Boolean");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF hex string object
////////////////////////////////////////////////////////////////////

public class PdfHex : PdfBase
	{
	public String HexStrValue;
	public PdfHex(String HexStrValue)
		{
		this.HexStrValue = HexStrValue;
		return;
		}
	public override String ToString()
		{
		return(HexStrValue);
		}
	public override String TypeToString()
		{
		return("Hex String");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF integer object
////////////////////////////////////////////////////////////////////

public class PdfInt : PdfBase
	{
	public Int32 IntValue;
	public PdfInt(Int32 IntValue)
		{
		this.IntValue = IntValue;
		return;
		}
	public override String ToString()
		{
		return(IntValue.ToString());
		}
	public override String TypeToString()
		{
		return("Integer");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF name object
////////////////////////////////////////////////////////////////////

public class PdfName : PdfBase
	{
	public String NameValue;
	public PdfName(String NameValue)
		{
		this.NameValue = NameValue;
		return;
		}
	public override String ToString()
		{
		return(NameValue);
		}
	public override String TypeToString()
		{
		return("Name");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF null object
////////////////////////////////////////////////////////////////////

public class PdfNull : PdfBase
	{
	public PdfNull() {}
	public override String ToString()
		{
		return("null");
		}
	public override String TypeToString()
		{
		return("Null");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF real number object
////////////////////////////////////////////////////////////////////

public class PdfReal : PdfBase
	{
	public Single RealValue;
	public PdfReal(Single RealValue)
		{
		this.RealValue = RealValue;
		return;
		}
	public override String ToString()
		{
		return(RealValue.ToString("G"));
		}
	public override String TypeToString()
		{
		return("Real");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF reference to an indirect object
////////////////////////////////////////////////////////////////////

public class PdfRef : PdfBase
	{
	public Int32 ObjNoValue;
	public PdfRef(Int32 ObjNoValue)
		{
		this.ObjNoValue = ObjNoValue;
		return;
		}
	public override String ToString()
		{
		return(ObjNoValue.ToString() + " 0 R");
		}
	public override String TypeToString()
		{
		return("Reference");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF string object
////////////////////////////////////////////////////////////////////

public class PdfStr : PdfBase
	{
	public String StrValue;
	public PdfStr(String StrValue)
		{
		this.StrValue = StrValue;
		return;
		}
	public override String ToString()
		{
		return(StrValue);
		}
	public override String TypeToString()
		{
		return("String");
		}
	}

////////////////////////////////////////////////////////////////////
// Enumeration of key words for PDF key word object
////////////////////////////////////////////////////////////////////

public enum KeyWord
	{
	Undefined,
	Stream,
	EndStream,
	EndObj,
	Xref,
	Trailer,
	N,
	F,
	}

////////////////////////////////////////////////////////////////////
// PDF key word object
////////////////////////////////////////////////////////////////////

public class PdfKeyword : PdfBase
	{
	public KeyWord KeywordValue;
	public PdfKeyword(KeyWord KeywordValue)
		{
		this.KeywordValue = KeywordValue;
		return;
		}
	public override String ToString()
		{
		return(KeywordValue.ToString());
		}
	public override String TypeToString()
		{
		return("Keyword");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF contents operator object
////////////////////////////////////////////////////////////////////

public class PdfOp : PdfBase
	{
	public Operator OpValue;
	public PdfOp(Operator OpValue)
		{
		this.OpValue = OpValue;
		return;
		}
	public override String ToString()
		{
		return(PdfParser.OperatorCode(OpValue));
		}
	public override String TypeToString()
		{
		return("Operator");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF array object
////////////////////////////////////////////////////////////////////

public class PdfArray : PdfBase
	{
	public PdfBase[]	ArrayValue;
	public PdfArray(PdfBase[] ArrayValue)
		{
		this.ArrayValue = ArrayValue;
		return;
		}
	public override String ToString()
		{
		StringBuilder Str = new StringBuilder("[");
		Int32 LastEolPtr = 0;
		foreach(PdfBase Obj in ArrayValue)
			{
			String ObjStr = Obj.ToString();
			if(Str.Length + ObjStr.Length > LastEolPtr + 80)
				{
				Str.Append('\n');
				LastEolPtr = Str.Length;
				}
			else if(!PdfParser.IsDelimiter(ObjStr[0]) && !PdfParser.IsDelimiter(Str[Str.Length - 1]))
				{
				Str.Append(' ');
				}
			Str.Append(ObjStr);
			}

		Str.Append("]");
		return(Str.ToString());
		}
	public override String TypeToString()
		{
		return("Array");
		}
	}

////////////////////////////////////////////////////////////////////
// Dictionary pair key and value
////////////////////////////////////////////////////////////////////

public class PdfPair : IComparable<PdfPair>
	{
	public String		Key;
	public PdfBase		ObjValue;

	public PdfPair
			(
			String	Key,
			PdfBase	ObjValue
			)
		{
		this.Key = Key;
		this.ObjValue = ObjValue;
		return;
		}

	public PdfPair
			(
			String	Key
			)
		{
		this.Key = Key;
		return;
		}

	public Int32 CompareTo
			(
			PdfPair	Other
			)
		{
		return(String.Compare(this.Key, Other.Key));
		}

	public override String ToString()
		{
		String ObjValStr = ObjValue.ToString();
		if(PdfParser.IsDelimiter(ObjValStr[0])) return(Key + ObjValStr);
		return(Key + " " + ObjValStr);
		}
	}

////////////////////////////////////////////////////////////////////
// PDF dictionary object
////////////////////////////////////////////////////////////////////

public class PdfDict : PdfBase
	{
	public PdfPair[] DictValue;

	public PdfDict
			(
			PdfPair[] DictValue
			)
		{
		this.DictValue = DictValue;
		}

	// search dictionary for key and return a value
	public PdfBase GetValue
			(
			String	Key
			)
		{
		Int32 Index = Array.BinarySearch(DictValue, new PdfPair(Key));
		return(Index < 0 ? PdfBase.Empty : DictValue[Index].ObjValue);
		}

	// search dictionary for a key. If the result is a name or array of names
	// return an array of names
	public Boolean GetArrayOfNames
			(
			String		 Key,
			out String[] Result
			)
		{
		// search for key and get value
		PdfBase Value = GetValue(Key);

		// look for single name object
		String Name = Value.ToName;
		if(Name != null)
			{
			Result = new String[] {Name};
			return(true);
			}

		// look for array of objects
		PdfBase[] ObjArray = Value.ToArray;
		if(ObjArray != null && ObjArray.Length != 0)
			{
			// define array of names
			String[] Names = new String[ObjArray.Length];

			// make sure all array items are names
			Int32 Index;
			for(Index = 0; Index < ObjArray.Length; Index++) if((Names[Index] = ObjArray[Index].ToName) == null) break;
			if(Index == ObjArray.Length)
				{
				Result = Names;
				return(true);
				}
			}

		// not found
		Result = null;
		return(false);
		}

	// convert dictionary to string
	public override String ToString()
		{
		StringBuilder Str = new StringBuilder("<<");
		Int32 Ptr = 0;
		foreach(PdfPair Pair in DictValue)
			{
			String ObjStr = Pair.ToString();
			if(Str.Length + ObjStr.Length > Ptr + 80)
				{
				Str.Append('\n');
				Ptr = Str.Length;
				}
			else if(!PdfParser.IsDelimiter(ObjStr[0]) && !PdfParser.IsDelimiter(Str[Str.Length - 1])) Str.Append(' ');
			Str.Append(ObjStr);
			}

		Str.Append(">>");
		return(Str.ToString());
		}

	public override String TypeToString()
		{
		return("Dictionary");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF Streem Object
////////////////////////////////////////////////////////////////////

public class PdfStream : PdfBase
	{
	public PdfDict	Dictionary;
	public Byte[]	Contents;
	public Int32	Position;
	public Int32	Length;

	public PdfStream
			(
			PdfDict		Dictionary,
			Int32		Position
			)
		{
		this.Dictionary = Dictionary;
		this.Position = Position;
		}

	public override String TypeToString()
		{
		return("Stream");
		}
	}

////////////////////////////////////////////////////////////////////
// PDF Inline Image Object
////////////////////////////////////////////////////////////////////

public class PdfInlineImage : PdfBase
	{
	public PdfDict	Dictionary;
	public Byte[]	Image;

	public PdfInlineImage
			(
			PdfDict		Dictionary,
			Byte[]		Image
			)
		{
		this.Dictionary = Dictionary;
		this.Image = Image;
		}

	// convert dictionary to string
	public override String ToString()
		{
		StringBuilder Str = new StringBuilder("Begin Inline Image\n");
		Str.Append(Dictionary.ToString());
		Str.Append('\n');

		Byte[] HexLine = new Byte[16];

		// loop for multiple of 16 bytes
		Int32 Length = Image.Length & ~15;
		for(Int32 Pos = 0; Pos < Length; Pos += 16)
			{
			Array.Copy(Image, Pos, HexLine, 0, 16);
			FormatOneLine(Str, Pos, HexLine);
			}

		// last partial line
		Int32 Extra = Image.Length - Length;
		if(Extra > 0)
			{
			// The start of the formatted line is correct. The end is left over from previous line
			Int32 Ptr = Str.Length;
			Array.Copy(Image, Length, HexLine, 0, Extra);
			FormatOneLine(Str, Length, HexLine);

			// erase the portion after the end of the file
			Ptr += 10 + 3 * Extra + (Extra > 7 ? 1 : 0);
			Str.Remove(Ptr, Str.Length - Ptr);
			Str.Append('\n');
			}
 
		Str.Append("End Inline Image\n");
		return(Str.ToString());
		}

	private void FormatOneLine
			(
			StringBuilder	Text,
			Int32			Pos,
			Byte[]			Hex
			)
		{
		Text.Append(String.Format("{0:X8}  {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2} {7:X2} {8:X2}  " +
				"{9:X2} {10:X2} {11:X2} {12:X2} {13:X2} {14:X2} {15:X2} {16:X2}\n",
				Pos, Hex[0], Hex[1], Hex[2], Hex[3], Hex[4], Hex[5], Hex[6], Hex[7], Hex[8], Hex[9], Hex[10],
				Hex[11], Hex[12], Hex[13], Hex[14], Hex[15]));
		return;
		}

	public override String TypeToString()
		{
		return("Inline Image");
		}
	}
}

