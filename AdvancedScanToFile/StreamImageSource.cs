using System;
using Atalasoft.Imaging;
using System.IO;

namespace Atalasoft.Examples
{
    public class StreamImageSource : RandomAccessImageSource
    {

        Stream _doc;
        int _currentindex;
        int _pageCount;
        bool _disposeStreamOnObjectDispose;

        /// <summary>
        /// Use the bool disposeStreamOnObjectDispose to explicitly control whether we will
        /// be disposing of the stream for you when finished or if you will manage disposal yourself
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="disposeStreamOnObjectDispose"></param>
        public StreamImageSource(Stream doc, bool disposeStreamOnObjectDispose)
        {
            _doc = doc;
            _currentindex = 0;
            _disposeStreamOnObjectDispose = disposeStreamOnObjectDispose;

            _doc.Seek(0, SeekOrigin.Begin);
            _pageCount = Atalasoft.Imaging.Codec.RegisteredDecoders.GetImageInfo(doc).FrameCount;
            _doc.Seek(0, SeekOrigin.Begin);

        }

        /// <summary>
        /// When using This constructor - we do NOT dispose of the stream
        /// It is your responsibility to manage your Stream disposal after this object is disposed
        /// 
        /// If you wish to specify disposal use StreamImageSource(Stream doc, bool disposeStreamOnObjectDispose)
        /// </summary>
        /// <param name="doc"></param>
        public StreamImageSource(Stream doc) : this(doc, false)
        {
            // This constructor is a convenience to mimic the behavior of our PdfImageSource
            // which does not dispose of the stream for you...
        }

        /// <summary>
        /// Allows usage of this class with a Byte Arrray ( byte[] )
        /// </summary>
        /// <param name="bytes"></param>
        public StreamImageSource(byte[] bytes) : this(new MemoryStream(bytes), true )
        {
            // Byte Arrays are awfully common means of passing documents about
            // this single constructor line will allow you to create this ImageSource
            // directly fron byte[] of any supported image file format
            //
            // Since we are creating the stream on construct there will be no outside
            // exposure to it so we will always dispose it ourselves
        }

        protected override ImageSourceNode LowLevelAcquire(int index)
        {
            _doc.Seek(0, SeekOrigin.Begin);
            ImageSourceNode node = new ImageSourceNode(new AtalaImage(_doc, index, null), null);
            return node;
        }

        protected override ImageSourceNode LowLevelAcquireNextImage()
        {
            return LowLevelAcquire(_currentindex++);
        }

        protected override void LowLevelDispose()
        {
            // we will ONLY dispose of the stream if we were explicitly told to
            if (_disposeStreamOnObjectDispose)
            {
                _doc.Dispose();
            }
        }

        protected override bool LowLevelFlushOnReset()
        {
            return true;
        }

        protected override bool LowLevelHasMoreImages()
        {
            return _currentindex <= _pageCount - 1;
        }

        protected override void LowLevelReset()
        {
            _currentindex = 0;
        }

        protected override void LowLevelSkipNextImage()
        {
            _currentindex++;
        }

        protected override int LowLevelTotalImages()
        {
            return _pageCount;
        }
    }
}
