/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	PdfIndirectObject
//	Class encapsulating a PDF indirect object.
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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace PdfFileAnalyzer
{
public enum FileType
	{
	Binary,
	Text,
	Image,
	Font,
	XRef,
	}

////////////////////////////////////////////////////////////////////
// Indirect Object
////////////////////////////////////////////////////////////////////

public class PdfIndirectObject : PdfBase, IComparable<PdfIndirectObject>
	{
	////////////////////////////////////////////////////////////////////
	// Members
	////////////////////////////////////////////////////////////////////

	public Int32			ObjectNo;
	public Int32			ParentObjectNo;
	public Int32			ParentObjectIndex;
	public String			ObjectType;
	public String			ObjectSubtype;
	public Int32			FilePosition;
	public PdfBase			ObjectValue;
	public Byte[]			PageContents;
	public Byte[]			PageSource;
	public String			FileName;
	public FileType			FileType;

	private PdfDocument		Document;			// PDF parent document

	////////////////////////////////////////////////////////////////////
	// Constructor for old style cross reference
	////////////////////////////////////////////////////////////////////

	public PdfIndirectObject
			(
			PdfDocument	Document,
			Int32		ObjectNumber,
			Int32		FilePosition
			)
		{
		// save document link
		this.Document = Document;

		// save object number
		this.ObjectNo = ObjectNumber;

		// save object position
		this.FilePosition = FilePosition;

		// empty object value
		this.ObjectValue = PdfBase.Empty;

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Constructor for stream style cross reference
	////////////////////////////////////////////////////////////////////

	public PdfIndirectObject
			(
			PdfDocument	Document,
			Int32		ObjectNumber,
			Int32		ParentObjectNo,
			Int32		ParentObjectIndex
			)
		{
		// save document link
		this.Document = Document;

		// save object number
		this.ObjectNo = ObjectNumber;

		// save parent number and position
		this.ParentObjectNo = ParentObjectNo;
		this.ParentObjectIndex = ParentObjectIndex;

		// empty object value
		this.ObjectValue = PdfBase.Empty;

		// exit
		return;
		}
	////////////////////////////////////////////////////////////////////
	// Constructor for binary search
	////////////////////////////////////////////////////////////////////

	public PdfIndirectObject
			(
			Int32		ObjectNumber
			)
		{
		// save object number
		this.ObjectNo = ObjectNumber;

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Compare PDF object used for resource objects
	////////////////////////////////////////////////////////////////////

	public Int32 CompareTo
			(
			PdfIndirectObject Other
			)
		{
		return(this.ObjectNo - Other.ObjectNo);
		}

	////////////////////////////////////////////////////////////////////
	// Read object
	////////////////////////////////////////////////////////////////////
	
	public void ReadObject()
		{
		// skip if done already or child of object stream
		if(!ObjectValue.IsEmpty || ParentObjectNo != 0) return;

		// set file position
		Document.SetFilePosition(FilePosition);

		// read first byte
		Document.ReadFirstChar();

		// first token must be object number "nnn 0 obj"
		if(Document.ParseNextItem().ToObjectNo != ObjectNo) throw new ApplicationException("Reading object header failed");

		// read next token
		ObjectValue = Document.ParseNextItem();

		// we have a dictionary
		if(ObjectValue.IsDictionary)
			{
			// set object type if available in the dictionary
			ObjectType = ObjectValue.ToDictionary.GetValue("/Type").ToName;

			// set object subtype if available in the dictionary
			ObjectSubtype = ObjectValue.ToDictionary.GetValue("/Subtype").ToName;

			// read next token after the dictionary
			KeyWord KeyWord = Document.ParseNextItem().ToKeyWord;

			// test for stream (change object from dictionary to stream)
			if(KeyWord == KeyWord.Stream) ObjectValue = new PdfStream(ObjectValue.ToDictionary, Document.GetFilePosition());

			// test for endobj
			else if(KeyWord != KeyWord.EndObj) throw new ApplicationException("'endobj' token is missing");
			}

		// object is not a dictionary
		else
			{
			// test for endobj 
			if(Document.ParseNextItem().ToKeyWord != KeyWord.EndObj) throw new ApplicationException("'endobj' token is missing");
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// process cross reference object stream
	////////////////////////////////////////////////////////////////////
	
	public void ProcessObjectStream()
		{
		// get the count of objects in this cross reference object stream
		Int32 ObjectCount;
		if(!ObjectValue.StreamDictionary.GetValue("/N").GetInteger(out ObjectCount) || ObjectCount <= 0)
			throw new ApplicationException("Cross reference object stream: count (/N) is missing");
 
		// get first byte offset
		Int32 FirstPos;
		if(!ObjectValue.StreamDictionary.GetValue("/First").GetInteger(out FirstPos))
			throw new ApplicationException("Cross reference object stream: first byte offset (/First) is missing");

		// no support for /Extends
		if(!ObjectValue.StreamDictionary.GetValue("/Extends").IsEmpty)
			throw new ApplicationException("Cross reference object stream: no support for /Extends");

		// create temp array of child objects
		PdfIndirectObject[] Children = new PdfIndirectObject[ObjectCount];

		// read all byte offset array
		PdfByteArrayParser PC = new PdfByteArrayParser(ObjectValue.StreamContents, false);
		PC.ReadFirstChar();
		for(Int32 Index = 0; Index < ObjectCount; Index++)
			{
			// object number
			Int32 ObjNo;
			if(!PC.ParseNextItem().GetInteger(out ObjNo))
				throw new ApplicationException("Cross reference object stream: object number error");
	
			// object offset
			Int32 ObjPos;
			if(!PC.ParseNextItem().GetInteger(out ObjPos))
				throw new ApplicationException("Cross reference object stream: object offset error");

			// find object
			PdfIndirectObject Child = Document.FindObject(ObjNo);
				if(Child == null) throw new ApplicationException("Cross reference object stream: object not found");

			// save child
			Children[Index] = Child;

			// save position
			Child.FilePosition = FirstPos + ObjPos;
			}

		// copy the object from the stream to the corresponding indirect object
		for(Int32 Index = 0; Index < ObjectCount; Index++)
			{
			PC.SetPos(Children[Index].FilePosition);
			PC.ReadFirstChar();
			Children[Index].ObjectValue = PC.ParseNextItem();
			}

		return;
		}

	////////////////////////////////////////////////////////////////////
	// Read stream
	////////////////////////////////////////////////////////////////////
	
	public void ReadStream()
		{
		// already done
		if(ObjectValue.StreamContents != null) return;

		// set file position
		Document.SetFilePosition(ObjectValue.StreamPosition);

		// look for length
		ObjectValue.StreamLength = Document.GetStreamLength(ObjectValue.StreamDictionary);

		// read object stream
		ObjectValue.StreamContents = Document.ReadBytes(ObjectValue.StreamLength);

		// saved flag
		Boolean Saved = false;

		// look for filter
		String[] FilterNames;
		if(ObjectValue.StreamDictionary.GetArrayOfNames("/Filter", out FilterNames))
			{
			// loop for each filter
			for(Int32 Index = 0; Index < FilterNames.Length; Index++)
				{
				String FilterName = FilterNames[Index];
				if(FilterName == "/FlateDecode")
					{
					// decompress and replace contents
					ObjectValue.StreamContents = FlateDecode(ObjectValue.StreamContents);
					Byte[] TempContents = PredictorDecode(ObjectValue.StreamContents);
					if(TempContents == null) break;
					ObjectValue.StreamContents = TempContents;
					}
				else if(FilterName == "/LZWDecode")
					{
					// decompress and replace contents
					ObjectValue.StreamContents = LZWDecode(ObjectValue.StreamContents);
					Byte[] TempContents = PredictorDecode(ObjectValue.StreamContents);
					if(TempContents == null) break;
					ObjectValue.StreamContents = TempContents;
					}
				else if(FilterName == "/ASCII85Decode")
					{
					// decode and replace contents
					ObjectValue.StreamContents = Ascii85Decode(ObjectValue.StreamContents);
					}
				else if(FilterName == "/DCTDecode")
					{
					// save as a JPEG file
					SaveJpg();
					Saved = true;
					break;
					}
				else
					{
					// unsupported filter
					break; //throw new ApplicationException("Unsupported stream filter: " + FilterName);
					}
				}
			}

		// save contents
		if(!Saved) SaveStreamContents();

		// verify end of stream
		// read first byte
		Document.ReadFirstChar();

		// test for endstream 
		if(Document.ParseNextItem().ToKeyWord != KeyWord.EndStream) throw new ApplicationException("Endstream token missing");

		// test for endobj 
		if(Document.ParseNextItem().ToKeyWord != KeyWord.EndObj) throw new ApplicationException("Endobj token missing");

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Add Contents to page
	// Page object has no stream. It has one or more contents objects.
	// This method concatenate all contents objects associated with this page.
	////////////////////////////////////////////////////////////////////

	public void AddContentsToPage
			(
			PdfIndirectObject		ContentsObject
			)
		{
		// current page has no contents
		if(PageContents == null)
			{
			// first time. page contents is empty
			PageContents = ContentsObject.ObjectValue.StreamContents;
			}

		// add more contents to the page
		else
			{
			// create new array to accomodate existing contents, additional contents and separating new line
			Byte[] TempContents = new Byte[PageContents.Length + ContentsObject.ObjectValue.StreamContents.Length + 1];

			// copy exiting contents
			Array.Copy(PageContents, TempContents, PageContents.Length);

			// add new line if required
			TempContents[PageContents.Length] = (Byte) '\n';

			// add new contents
			Array.Copy(ContentsObject.ObjectValue.StreamContents, 0, TempContents, PageContents.Length + 1,
				ContentsObject.ObjectValue.StreamContents.Length);

			// replace existing contents with updated array
			PageContents = TempContents;
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Process Contents
	////////////////////////////////////////////////////////////////////

	public void SaveStreamContents()
		{
		// test for bitmap image
		Int32 BitsPerComponent;
		ObjectValue.StreamDictionary.GetValue("/BitsPerComponent").GetInteger(out BitsPerComponent);

		Int32 ImageWidth;
		ObjectValue.StreamDictionary.GetValue("/Width").GetInteger(out ImageWidth);

		Int32 ImageHeight;
		ObjectValue.StreamDictionary.GetValue("/Height").GetInteger(out ImageHeight);

		String ColorSpace = ObjectValue.StreamDictionary.GetValue("/ColorSpace").ToName;

		// test for image
		if(BitsPerComponent == 8 && ImageWidth > 0 && ImageHeight > 0 && ColorSpace != null &&
			(ColorSpace == "/DeviceRGB" || ColorSpace == "/DeviceGray"))
			{
			FileType = FileType.Image;
			if(ColorSpace == "/DeviceRGB") SaveRGBBitmap(ImageWidth, ImageHeight);
			else SaveGrayBitmap(ImageWidth, ImageHeight);
			return;
			}

		// file extension
		String Ext;

		// test for cross reference
		if(ObjectType == "/XRef")
			{
			FileType = FileType.XRef;
			Ext = "xref";
			}

		// test for true type font
		else if(ObjectType == "/FontFile2" || ObjectType == "/FontFile3")
			{
			FileType = FileType.Font;
			Ext = "ttf";
			}

		// all others
		else
			{
			// test for binary file
			Int32 NonText = 0;
			foreach(Byte Chr in ObjectValue.StreamContents) if(Chr < ' ' && !PdfParser.IsWhiteSpace(Chr) || Chr > '~') NonText++;

			// set file type;
			if(NonText > ObjectValue.StreamContents.Length / 20)
				{
				FileType = FileType.Binary;
				Ext = "bin";
				}
			else
				{
				FileType = FileType.Text;
				Ext = "txt";
				}
			}

		// file name
		FileName = String.Format("StreamObj_{0}.{1}", ObjectNo, Ext);
		String FullName = Document.ResultFolderName + "\\" + FileName;

		// save contents to the disk
		using (BinaryWriter PrintFile =
			new BinaryWriter(new FileStream(FullName, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.UTF8))
			{		
			PrintFile.Write(ObjectValue.StreamContents);
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Save stream with unsuported filter
	////////////////////////////////////////////////////////////////////

	public void SaveBinStream()
		{
		// file type
		FileType = FileType.Binary;

		// file name
		FileName = String.Format("StreamObj_{0}.bin", ObjectNo);
		String FullName = Document.ResultFolderName + "\\" + FileName;

		// save contents to the disk
		using (BinaryWriter PrintFile =
			new BinaryWriter(new FileStream(FullName, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.UTF8))
			{		
			PrintFile.Write(ObjectValue.StreamContents);
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Process Contents
	////////////////////////////////////////////////////////////////////

	public void SavePageContents()
		{
		// set file type
		FileType = FileType.Text;

		// file name
		FileName = String.Format("PageObj_{0}.txt", ObjectNo);
		String FullName = Document.ResultFolderName + "\\" + FileName;

		// save contents to the disk
		using (BinaryWriter PrintFile =
			new BinaryWriter(new FileStream(FullName, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.UTF8))
			{		
			PrintFile.Write(PageContents);
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Process Contents
	////////////////////////////////////////////////////////////////////

	public void ParsePageContents()
		{
		// create parse contents object and read first character
		PdfByteArrayParser PC = new PdfByteArrayParser(PageContents, true);
		PC.ReadFirstChar();

		List<PdfBase> ArgStack = new List<PdfBase>();
		PdfBase Token;
		StringBuilder Text = new StringBuilder();

		// loop for operators
		for(;;)
			{
			ArgStack.Clear();

			// loop for arguments and exit on operator
			for(;;)
				{
				Token = PC.ParseNextItem();
				if(Token.IsOperator || Token.IsInlineImage || Token.IsEmpty) break;
				ArgStack.Add(Token);
				}

			// end of contents
			if(Token.IsEmpty) break;

			// inline image
			if(Token.IsInlineImage)
				{
				Text.Append(Token.ToString());
				continue;
				}

			// format command
			FormatCommand(Text, ArgStack, Token.ToOperator);
			}			

		// file name
		String FullName = String.Format("{0}\\PageSource_{1}.txt", Document.ResultFolderName, ObjectNo);

		// save contents to the disk
		using(StreamWriter PrintFile = new StreamWriter(FullName)) PrintFile.Write(Text.ToString());

		// save in Byte array
		PageSource = new Byte[Text.Length];
		for(Int32 Index = 0; Index < Text.Length; Index++) PageSource[Index] = (Byte) Text[Index];

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Format command
	////////////////////////////////////////////////////////////////////

	public void FormatCommand
			(
			StringBuilder	Text,
			List<PdfBase>	ArgStack,
			Operator		Operator
			)
		{
		// no arguments
		if(ArgStack.Count == 0)
			{
			Text.AppendFormat("{0}(); // {1}\r\n", Operator.ToString(), PdfParser.OperatorCode(Operator));
			return;
			}

		// all arguments are integers or real
		Int32 Index;
		for(Index = 0; Index < ArgStack.Count && ArgStack[Index].IsNumber; Index++);
		if(Index == ArgStack.Count)
			{
			Text.AppendFormat("{0}({1:G}", Operator.ToString(), ArgStack[0].ToNumber);
			for(Index = 1; Index < ArgStack.Count; Index++)
				Text.AppendFormat(", {0:G}", ArgStack[Index].ToNumber);
			Text.AppendFormat("); // {0}\r\n", PdfParser.OperatorCode(Operator));
			return;
			}

		// all others
		Text.AppendFormat("{0}(", Operator.ToString());
		for(Index = 0; Index < ArgStack.Count; Index++)
			{
			if(Index != 0) Text.Append(", ");
			if(ArgStack[Index].IsNumber)
				Text.AppendFormat("{0:G}", ArgStack[Index].ToNumber);
			else
				Text.AppendFormat("\"{0}\"", ArgStack[Index].ToString());
			}
		Text.AppendFormat("); // {0}\r\n", PdfParser.OperatorCode(Operator));

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Create bitmap file from bitmap data
	////////////////////////////////////////////////////////////////////

	public void SaveRGBBitmap
			(
			Int32 ImageWidth,
			Int32 ImageHeight
			)
		{
		// create empty bitmap
		Bitmap BM = new Bitmap(ImageWidth, ImageHeight, PixelFormat.Format24bppRgb);

		// create a new contents array with bmp width
		Byte[] PixelBuf = new Byte[((3 * ImageWidth + 3) & ~3) * ImageHeight];

		// copy row by row
		Int32 IPtr = 0;
		Int32 BPtr = 0;
		for(Int32 Row = 0; Row < ImageHeight; Row++)
			{
			// copy column by column
			for(Int32 Col = 0; Col < ImageWidth; Col++)
				{
				PixelBuf[BPtr + 2] = ObjectValue.StreamContents[IPtr++];
				PixelBuf[BPtr + 1] = ObjectValue.StreamContents[IPtr++];
				PixelBuf[BPtr] = ObjectValue.StreamContents[IPtr++];
				BPtr += 3;
				}
			BPtr = (BPtr + 3) & ~3;
			}

		// Lock the bitmap's bits.  
		Rectangle LockRect = new Rectangle(0, 0, ImageWidth, ImageHeight);
		BitmapData BmpData = BM.LockBits(LockRect, ImageLockMode.WriteOnly, BM.PixelFormat);

		// Get the address of the first line.
		IntPtr ImagePtr = BmpData.Scan0;

		// Copy contents into the bitmap
		Marshal.Copy(PixelBuf, 0, ImagePtr, PixelBuf.Length);

		// unlock the bitmap
		BM.UnlockBits(BmpData);

		// save image to the disk
		FileName = String.Format("ImageRGBObj_{0}.bmp", ObjectNo);
		String FullName = Document.ResultFolderName + "\\" + FileName;
		BM.Save(FullName, ImageFormat.Bmp);

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Create bitmap file from gray bitmap data
	////////////////////////////////////////////////////////////////////

	public void SaveGrayBitmap
			(
			Int32 ImageWidth,
			Int32 ImageHeight
			)
		{
		// create empty bitmap
		Bitmap BM = new Bitmap(ImageWidth, ImageHeight, PixelFormat.Format24bppRgb);

		// create a new contents array with bmp width
		Byte[] PixelBuf = new Byte[((3 * ImageWidth + 3) & ~3) * ImageHeight];

		// copy row by row
		Int32 IPtr = 0;
		Int32 BPtr = 0;
		for(Int32 Row = 0; Row < ImageHeight; Row++)
			{
			// copy column by column
			for(Int32 Col = 0; Col < ImageWidth; Col++)
				{
				PixelBuf[BPtr + 2] = ObjectValue.StreamContents[IPtr];
				PixelBuf[BPtr + 1] = ObjectValue.StreamContents[IPtr];
				PixelBuf[BPtr] = ObjectValue.StreamContents[IPtr++];
				BPtr += 3;
				}
			BPtr = (BPtr + 3) & ~3;
			}

		// Lock the bitmap's bits.  
		Rectangle LockRect = new Rectangle(0, 0, ImageWidth, ImageHeight);
		BitmapData BmpData = BM.LockBits(LockRect, ImageLockMode.WriteOnly, BM.PixelFormat);

		// Get the address of the first line.
		IntPtr ImagePtr = BmpData.Scan0;

		// Copy contents into the bitmap
		Marshal.Copy(PixelBuf, 0, ImagePtr, PixelBuf.Length);

		// unlock the bitmap
		BM.UnlockBits(BmpData);

		// save image to the disk
		FileName = String.Format("ImageGrayObj_{0}.bmp", ObjectNo);
		String FullName = Document.ResultFolderName + "\\" + FileName;
		BM.Save(FullName, ImageFormat.Bmp);

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Create JPEG file from image data
	////////////////////////////////////////////////////////////////////

	public void SaveJpg()
		{
		// image file type
		FileType = FileType.Image;

		// file name
		FileName = String.Format("ImageObj_{0}.jpg", ObjectNo);
		String FullName = Document.ResultFolderName + "\\" + FileName;

		// save image to the disk
		using (BinaryWriter PrintFile =
			new BinaryWriter(new FileStream(FullName, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.UTF8))
			{		
			PrintFile.Write(ObjectValue.StreamContents);
			}

		// exit
		return;
		}

	////////////////////////////////////////////////////////////////////
	// Write indirect object to object analysis file
	////////////////////////////////////////////////////////////////////

	public override String  ToString()
		{
		StringBuilder TextFile = new StringBuilder();

		// write object header
		TextFile.AppendFormat("{0:0;TR0; } 0 obj\n", ObjectNo);
		TextFile.AppendFormat("%% File Position: {0} Hex: {0:X}\n", FilePosition);
		TextFile.AppendFormat("%% Object Type: {0}\n", String.IsNullOrEmpty(ObjectType) ? "/Unknown" : ObjectType);
		if(!String.IsNullOrEmpty(ObjectSubtype)) TextFile.AppendFormat("%% Object Subtype: {0}\n", ObjectSubtype);

		// stream
		if(ObjectValue.IsStream)
			{
			// write to pdf file
			TextFile.Append(ObjectValue.StreamDictionary.ToString());

			// final terminator
			TextFile.Append('\n');

			// stream place holder
			TextFile.Append("stream\n");
			TextFile.AppendFormat("Stream data file position: {0:#,###} Hex: {0:X}\n", ObjectValue.StreamPosition);
			TextFile.Append("endstream\n");
			}

		// object has contents that is not stream
		else
			{
			// write content to pdf file
			TextFile.Append(ObjectValue.ToString());

			// final terminator
			TextFile.Append('\n');
			}	

		// output object trailer
		TextFile.Append("endobj\n");
		return(TextFile.ToString());
		}

	////////////////////////////////////////////////////////////////////
	// Filter /FlateDecode
	////////////////////////////////////////////////////////////////////

	public Byte[] FlateDecode
			(
			Byte[] ReadBuffer
			)
		{
		// get ZLib header
		Int32 Header = (Int32) ReadBuffer[0] << 8 | ReadBuffer[1];

		// test header: chksum, compression method must be deflated, no support for external dictionary
		if(Header % 31 != 0 || (Header & 0xf00) != 0x800 && (Header & 0xf00) != 0 || (Header & 0x20) != 0)
			throw new ApplicationException("ZLIB file header is in error");

		// output buffer
		Byte[] OutputBuf;

		// decompress the file
		if((Header & 0xf00) == 0x800)
			{
			// create input stream
			MemoryStream InputStream = new MemoryStream(ReadBuffer, 2, ReadBuffer.Length - 6);

			// create output memory stream to receive the decompressed buffer
			MemoryStream OutputStream = new MemoryStream();

			// deflate decompression object
			DeflateStream Deflate = new DeflateStream(InputStream, CompressionMode.Decompress, true);
			Deflate.CopyTo(OutputStream);

			// decompressed file length
			Int32 OutputLen = (Int32) OutputStream.Length;

			// create output buffer
			OutputBuf = new Byte[OutputLen];

			// copy the compressed result
			OutputStream.Seek(0, SeekOrigin.Begin);
			OutputStream.Read(OutputBuf, 0, OutputLen);
			OutputStream.Close();
			}
		else
			{
			// no compression
			OutputBuf = new Byte[ReadBuffer.Length - 6];
			Array.Copy(ReadBuffer, 2, OutputBuf, 0, OutputBuf.Length);
			}

		// ZLib checksum is Adler32
		Int32 ReadPtr = ReadBuffer.Length - 4;
		if((((UInt32) ReadBuffer[ReadPtr++] << 24) | ((UInt32) ReadBuffer[ReadPtr++] << 16) |
			((UInt32) ReadBuffer[ReadPtr++] << 8) | ((UInt32) ReadBuffer[ReadPtr++])) != Adler32Checksum(OutputBuf))
				throw new ApplicationException("ZLIB file Adler32 test failed");

		// successful exit
		return(OutputBuf);
		}

	/////////////////////////////////////////////////////////////////////
	// Accumulate Adler Checksum
	/////////////////////////////////////////////////////////////////////

	public UInt32 Adler32Checksum
			(
			Byte[]	Buffer
			)
		{
		const UInt32 Adler32Base = 65521;

		// split current Adler chksum into two 
		UInt32 AdlerLow = 1;
		UInt32 AdlerHigh = 0;
		Int32 Len = Buffer.Length;
		Int32 Pos = 0;

		while(Len > 0) 
			{
			// We can defer the modulo operation:
			// Under worst case the starting value of the two halves is 65520 = (AdlerBase - 1)
			// each new byte is maximum 255
			// The low half grows AdlerLow(n) = AdlerBase - 1 + n * 255
			// The high half grows AdlerHigh(n) = (n + 1)*(AdlerBase - 1) + n * (n + 1) * 255 / 2
			// The maximum n before overflow of 32 bit unsigned integer is 5552
			// it is the solution of the following quadratic equation
			// 255 * n * n + (2 * (AdlerBase - 1) + 255) * n + 2 * (AdlerBase - 1 - UInt32.MaxValue) = 0
			Int32 n = Len < 5552 ? Len : 5552;
			Len -= n;
			while(--n >= 0) 
				{
				AdlerLow += (UInt32) Buffer[Pos++];
				AdlerHigh += AdlerLow;
				}
			AdlerLow %= Adler32Base;
			AdlerHigh %= Adler32Base;
			}
		return((AdlerHigh << 16) | AdlerLow);
		}

	////////////////////////////////////////////////////////////////////
	// Filter /LZWDecode
	////////////////////////////////////////////////////////////////////

	public Byte[] LZWDecode
			(
			Byte[] InputBuffer
			)
		{
		// decompress
		return(LZW.Decode(InputBuffer));
		}

	////////////////////////////////////////////////////////////////////
	// Filter "/DecodeParms"
	////////////////////////////////////////////////////////////////////

	public Byte[] PredictorDecode
			(
			Byte[]		InputBuffer
			)
		{
		// test for /DecodeParams
		PdfDict DecodeParms = ObjectValue.StreamDictionary.GetValue("/DecodeParms").ToDictionary;

		// none found
		if(DecodeParms == null) return(InputBuffer);

		// look for predictor code. if default (none or 1) do nothing
		Int32 Predictor;
		if(!DecodeParms.GetValue("/Predictor").GetInteger(out Predictor) || Predictor == 1) return(InputBuffer);

		// we only support predictor code 12
		if(Predictor != 12) return(null); // throw new ApplicationException("/DecodeParms /Predictor is not 12");

		// get width
		Int32 Width;
		DecodeParms.GetValue("/Columns").GetInteger(out Width);
		if(Width < 0) throw new ApplicationException("/DecodeParms /Columns is negative");
		if(Width == 0) Width = 1;

		// calculate rows
		Int32 Rows = InputBuffer.Length / (Width + 1);
		if(Rows < 1) throw new ApplicationException("/DecodeParms /Columns is greater than stream length");

		// create output buffer
		Byte[] OutputBuffer = new Byte[Rows * Width];

		// reset pointers
		Int32 InPtr = 1;
		Int32 OutPtr = 0;
		Int32 OutPrevPtr = 0;

		// first row (ignore filter)
		while(OutPtr < Width) OutputBuffer[OutPtr++] = InputBuffer[InPtr++];

		// decode loop
		for(Int32 Row = 1; Row < Rows; Row++)
			{
			// first byte is filter
			Int32 Filter = InputBuffer[InPtr++];

			// we support PNG filter up only
			if(Filter != 2) throw new ApplicationException("/DecodeParms Only supported filter is 2");

			// convert input to output
			for(int Index = 0; Index < Width; Index++) OutputBuffer[OutPtr++] = (Byte) (OutputBuffer[OutPrevPtr++] + InputBuffer[InPtr++]);
			}

		return(OutputBuffer);
		}

	////////////////////////////////////////////////////////////////////
	// Filter ASCII85Decode
	////////////////////////////////////////////////////////////////////

	public Byte[] Ascii85Decode
			(
			Byte[] InputBuffer
			)
		{
		// array of power of 85: 85**4, 85**3, 85**2, 85**1, 85**0
		UInt32[] Power85 = new UInt32[] {85*85*85*85, 85*85*85, 85*85, 85, 1}; 

		// output buffer
		List<Byte> OutputBuffer = new List<Byte>();

		// convert input to output buffer
		Int32 State = 0;
		UInt32 FourBytes = 0;
		for(Int32 Index = 0; Index < InputBuffer.Length; Index++)
			{
			// next character
			Char NextChar = (Char) InputBuffer[Index];

			// end of stream "~>"
			if(NextChar == '~') break;

			// ignore white space
			if(PdfParser.IsWhiteSpace(NextChar)) continue;

			// special case of four zero bytes
			if(NextChar == 'z' && State == 0)
				{
				OutputBuffer.Add(0);
				OutputBuffer.Add(0);
				OutputBuffer.Add(0);
				OutputBuffer.Add(0);
				continue;
				}

			// test for valid characters
			if(NextChar < '!' || NextChar > 'u') throw new ApplicationException("Illegal character in ASCII85Decode");

			// accumulate 4 output bytes from 5 input bytes
			FourBytes += Power85[State++] * (UInt32) (NextChar - '!');

			// we have 4 output bytes
			if(State == 5)
				{
				OutputBuffer.Add((Byte)(FourBytes >> 24));
				OutputBuffer.Add((Byte)(FourBytes >> 16));
				OutputBuffer.Add((Byte)(FourBytes >> 8));
				OutputBuffer.Add((Byte) FourBytes);

				// reset state
				State = 0;
				FourBytes = 0;
				}
			}

		// if state is not zero add one, two or three terminating bytes
		if(State != 0)
			{
			if(State == 1) throw new ApplicationException("Illegal length in ASCII85Decode");

			// add padding of 84
			for(Int32 PadState = State; PadState < 5; PadState++) FourBytes += Power85[PadState] * (UInt32) ('u' - '!');

			// add one, two or three terminating bytes
			OutputBuffer.Add((Byte)(FourBytes >> 24));
			if(State >= 3)
				{
				OutputBuffer.Add((Byte)(FourBytes >> 16));
				if(State >= 4) OutputBuffer.Add((Byte)(FourBytes >> 8));
				}
			}

		// exit
		return(OutputBuffer.ToArray());
		}
	}
}
