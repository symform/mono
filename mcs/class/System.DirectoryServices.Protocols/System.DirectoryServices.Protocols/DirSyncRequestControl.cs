//
// DirSyncRequestControl.cs
//
// Author:
//   Atsushi Enomoto  <atsushi@ximian.com>
//
// Copyright (C) 2009 Novell, Inc.
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Linq;

namespace System.DirectoryServices.Protocols
{
	public class DirSyncRequestControl : DirectoryControl
	{
		public DirSyncRequestControl ()
			: base (null, null, false, false)
		{
		}

		public DirSyncRequestControl (byte [] cookie)
			: this ()
		{
			Cookie = cookie;
		}

		public DirSyncRequestControl (byte [] cookie, DirectorySynchronizationOptions option)
			: this (cookie)
		{
			Option = option;
		}

		public DirSyncRequestControl (byte [] cookie, DirectorySynchronizationOptions option, int attributeCount)
			: this (cookie, option)
		{
			AttributeCount = attributeCount;
		}

		public int AttributeCount { get; set; }
		public byte [] Cookie { get; set; }
		public DirectorySynchronizationOptions Option { get; set; }

		public override byte [] GetValue ()
		{
			throw new NotImplementedException ();
		}
	}
}
