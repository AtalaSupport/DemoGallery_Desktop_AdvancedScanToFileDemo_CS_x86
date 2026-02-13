using System;
using Atalasoft.Imaging;
using System.IO;

namespace Atalasoft.Examples
{
    public class RangedStreamImageSource : RandomAccessImageSource
    {
        private int _start;
        public int Start
        {
            get { return this._start; }
            set { this._start = value; Reset(); }
        }

        private int _finish;
        public int Finish
        {
            get { return this._finish; }
            set { this._finish = value; Reset(); }
        }

        private Stream _doc;
        private int _currentindex;
        //private int _virtualCurrentIndex;
        private int _fullPageCount;
        private int _virtualPageCount;
        private bool _disposeStreamOnObjectDispose;

        /// <summary>
        /// Use the bool disposeStreamOnObjectDispose to explicitly control whether we will
        /// be disposing of the stream for you when finished or if you will manage disposal yourself
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="disposeStreamOnObjectDispose"></param>
        public RangedStreamImageSource(Stream doc, bool disposeStreamOnObjectDispose, int Start, int Finish)
        {
            //Console.WriteLine("RangedStreamImageSource() CTOR");
            //Console.WriteLine("  doc: " + doc.Length.ToString() + " bytes");
            //Console.WriteLine("  disposeStreamOnObjectDispose: " + disposeStreamOnObjectDispose.ToString());
            //Console.WriteLine("  Start: " + Start.ToString());
            //Console.WriteLine("  Finish: " + Finish.ToString());

            //Console.WriteLine("  Sanity Checking Start/Finish...");
            if (Finish < Start)
            {
                //Console.WriteLine("    Finish<Start.. resetting finish to " + Start.ToString());
                Finish = Start;
                //throw new System.ArgumentOutOfRangeException("Start must be < Finish");
            }
            if (Start < 0)
            {
                //Console.WriteLine("    Start < 0 .. resetting Start to 0");
                Start = 0;
                //throw new System.ArgumentOutOfRangeException("Start index must be 0 or greater");
            }


            this._start = Start;
            this._finish = Finish;



            this._doc = doc;
            this._currentindex = this._start;
            this._disposeStreamOnObjectDispose = disposeStreamOnObjectDispose;

            this._doc.Seek(0, SeekOrigin.Begin);
            this._fullPageCount = Atalasoft.Imaging.Codec.RegisteredDecoders.GetImageInfo(doc).FrameCount;
            this._doc.Seek(0, SeekOrigin.Begin);

            // protecting ourselves from going over the total
            //Console.WriteLine("  Sanity checking Finish vs page count...");
            if (this._finish >= this._fullPageCount)
            {
                //Console.WriteLine("    Invalid Finish: Adjusting to " + (this._fullPageCount - 1).ToString());
                this._finish = this._fullPageCount - 1;
            }

            this._virtualPageCount = 1 + this._finish - this._start;
            //this._virtualCurrentIndex = 0;

        }

        /// <summary>
        /// When using This constructor - we do NOT dispose of the stream
        /// It is your responsibility to manage your Stream disposal after this object is disposed
        /// 
        /// If you wish to specify disposal use StreamImageSource(Stream doc, bool disposeStreamOnObjectDispose)
        /// </summary>
        /// <param name="doc"></param>
        public RangedStreamImageSource(Stream doc, int Start, int Finish)
            : this(doc, false, Start, Finish)
        {
            // This constructor is a convenience to mimic the behavior of our PdfImageSource
            // which does not dispose of the stream for you...
        }

        /// <summary>
        /// Allows usage of this class with a Byte Arrray ( byte[] )
        /// </summary>
        /// <param name="bytes"></param>
        public RangedStreamImageSource(byte[] bytes, int Start, int Finish)
            : this(new MemoryStream(bytes), true, Start, Finish)
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
            return _currentindex <= _finish;
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
            return this._virtualPageCount;
        }
    }
}
