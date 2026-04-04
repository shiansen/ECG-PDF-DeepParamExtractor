/////////////////////////////////////////////////////////////////////
//
//	PdfFileAnalyzer
//	PDF file analysis program
//
//	LZWDecode
//	Class designed to decode LZW compressed string.
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

namespace PdfFileAnalyzer
{
public static class LZW
	{
	/////////////////////////////////////////////////////////////////
	// Decode LZW buffer
	/////////////////////////////////////////////////////////////////

	public static Byte[] Decode
			(
			Byte[] ReadBuffer
			)
		{
		// define two special codes
		const Int32	ResetDictionary	= 256;
		const Int32	EndOfStream = 257;

		// create new dictionary
		Byte[][] Dictionary = new Byte[4096][];

		// initialize first 256 entries
		for(Int32 Index = 0; Index < 256; Index++) Dictionary[Index] = new Byte[] {(Byte) Index};

		// output buffer
		List<Byte> WriteBuffer = new List<Byte>();
		
		// Initialize variables
		Int32 ReadPtr = 0;
		Int32 BitBuffer = 0;
		Int32 BitCount = 0;
		Int32 DictionaryPtr = 258;
		Int32 CodeLength = 9;
		Int32 CodeMask = 511;
		Int32 Code = 0;
		Int32 OldCode = -1;

		// loop for all codes in the buffer
		for(;;)
			{
			// fill the buffer such that it will contain 17 to 24 bits
			for(; BitCount <= 16 && ReadPtr < ReadBuffer.Length; BitCount += 8) BitBuffer = (BitBuffer << 8) | ReadBuffer[ReadPtr++];

			// for LZW blocks with missing end of block mark
			if(BitCount < CodeLength) break;

			// get next code
			Code = (BitBuffer >> (BitCount - CodeLength)) & CodeMask;
			BitCount -= CodeLength;

			// end of encoded area
			if(Code == EndOfStream) break;

			// reset dictionary
			if(Code == ResetDictionary)
				{
				DictionaryPtr = 258;
				CodeLength = 9;
				CodeMask = 511;
				OldCode = -1;
				continue;
				}

			// text to be added to output buffer
			Byte[] AddToOutput;

			// code is available in the dictionary
			if(Code < DictionaryPtr)
				{
				// text to be added to output buffer
				AddToOutput = Dictionary[Code];

				// first time after dictionary reset
				if(OldCode < 0)
					{
					WriteBuffer.AddRange(AddToOutput);
					OldCode = Code;
					continue;
					}

				// add new entry to dictionary
				// the previous match and the new first byte
				Dictionary[DictionaryPtr++] = BuildString(Dictionary[OldCode], AddToOutput[0]);
				}

			// special case repeating the same squence with first and last byte being the same
			else if(Code == DictionaryPtr)
				{
				// text to be added to output buffer
				AddToOutput = Dictionary[OldCode];
				AddToOutput = BuildString(AddToOutput, AddToOutput[0]);

				// add new entry to the dictionary
				Dictionary[DictionaryPtr++] = AddToOutput;
				}

			// code should not be greater than dictionary size
			else throw new ApplicationException("LZWDecode: Code error");

			// add to output buffer
			WriteBuffer.AddRange(AddToOutput);

			// save code
			OldCode = Code;

			// switch code length from 9 to 10, 11 and 12
			if(DictionaryPtr == 511 || DictionaryPtr == 1023 || DictionaryPtr == 2047)
				{
				CodeLength++;
				CodeMask = (CodeMask << 1) + 1;
				}
			}

		// return decoded byte array
		return(WriteBuffer.ToArray());
		}

	/////////////////////////////////////////////////////////////////
	// Build new dictionary string
	/////////////////////////////////////////////////////////////////

	private static Byte[] BuildString
			(
			Byte[]	OldString,
			Byte	AddedByte
			)
		{
		Int32 Length = OldString.Length;
		Byte[] NewString = new Byte[Length + 1];
		Array.Copy(OldString, 0, NewString, 0, Length);
		NewString[Length] = AddedByte;
		return(NewString);
		}
    }
}
