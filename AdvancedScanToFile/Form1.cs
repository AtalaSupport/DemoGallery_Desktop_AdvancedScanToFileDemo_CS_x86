using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Atalasoft.Imaging.WinControls;
using Atalasoft.Annotate.UI;
using Atalasoft.Annotate;
using Atalasoft.Imaging.Codec;
using Atalasoft.Twain;
using Atalasoft.Imaging;
using System.IO;
using Atalasoft.Imaging.Codec.Pdf;
using Atalasoft.Annotate.Formatters;
using Atalasoft.Annotate.Exporters;
using Atalasoft.Imaging.Drawing;
using Atalasoft.Imaging.ImageProcessing.Transforms;
using Atalasoft.Imaging.ImageProcessing;
using Atalasoft.Imaging.ImageProcessing.Document;


namespace AdvancedScanToFile
{
    public partial class Form1 : Form
    {
        Acquisition _acquisition;
        Device _device;
        ImageSource _imgsrc;
        MemoryStream _printMs;
        bool _shift = false;
        bool _ctrl = false;

        /**
         * We use a static constructor in order to ensure certain things are done 
         * BEFORE any other Atalasoft components are created/touched
         */
        static Form1()
        {
            // TROUBLESHOOTING TIP - IF your scanner is having issues try setting this to true
            Atalasoft.Twain.TwainManager.ForceTwain1xBehavior = false;
            /**
             * If you're going to use PDF files, add references to Atalasoft.dotImage.PdfReader.dll 
             * and  uncomment the following line of code:
             * requires a license for PdfReader.
             */
            RegisteredDecoders.Decoders.Add(new PdfDecoder() { Resolution = 200, RenderSettings = new RenderSettings() { AnnotationSettings = AnnotationRenderSettings.RenderNone } });
        }

        /**
         * This is the first place where the Atalasoft Components are available for
         * direct access. In this case, we're handling the auto zoom and telling the
         * annotation viewer to bring up the first page when opening a new item
         */
        public Form1()
        {
            InitializeComponent();

            // the next five items are specifically to enable mouse wheel scrolling and allow us to do horizontal scrolling oerride
            // ====================================================================
            this.KeyPreview = true;
            this.MouseWheel += Form1_MouseWheel;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp;
            this.documentAnnotationViewer1.ImageControl.MouseWheelScrolling = true;
            // ====================================================================

            documentAnnotationViewer1.SelectFirstPageOnOpen = true;
            documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.FitToWidth;

            // We Need to add the ability to directly handle embedded annotations
            int PdfRes = 96;
            foreach (object decoder in RegisteredDecoders.Decoders)
            {
                if (decoder.GetType() == typeof(PdfDecoder))
                {
                    PdfDecoder pdfDec = decoder as PdfDecoder;
                    PdfRes = pdfDec.Resolution;
                    break;
                }
            }
            this.documentAnnotationViewer1.AnnotationSaveOptionsHandler = annotationSaveOptionsHandler;
            this.documentAnnotationViewer1.AnnotationDataProvider = new Atalasoft.Annotate.UI.EmbeddedAnnotationDataProvider(new PointF(PdfRes, PdfRes));
            this.documentAnnotationViewer1.CreatePdfAnnotationDataExporter += new CreatePdfAnnotationDataExporterHandler(documentAnnotationViewer1_CreatePdfAnnotationDataExporter);
            

            this._acquisition = new Acquisition();
            if (this._acquisition.Devices.Default != null)
            {
                this._device = this._acquisition.Devices.Default;
            }
            this._acquisition.ImageAcquired += new ImageAcquiredEventHandler(_acquisition_ImageAcquired);
            this._acquisition.AcquireCanceled += new EventHandler(_acquisition_AcquireCanceled);
            this._acquisition.AcquireFinished += new EventHandler(_acquisition_AcquireFinished);
            this._acquisition.AsynchronousException += new AsynchronousExceptionEventHandler(_acquisition_AsynchronousException);
        }

        
        #region File Menu Events
        /**
         * Here's the basic technique for opening a file the viewer
         * this clears the existing images though.. use the add option for adding to the viewer
         */
        private void fileOpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    documentAnnotationViewer1.Open(dlg.FileName);
                }
            }
        }

        private void fileAddToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    documentAnnotationViewer1.Add(dlg.FileName, -1, "", "");
                }
            }
        }

        private void fileClearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.Clear();
        }

        /// <summary>
        /// Save As tiff or PDF .. no annotations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void fileSaveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // DotImage supports many different image formats, PDF, TIFF, PNG, BMP, JPEG, etc..
            // however not all image formats support multiple pages. Since this applciation is built 
            // with multipage support as a key feature, will use TIFF as it's a multipage image format
            // IF you wish, changing the encoder to a PdfEncoder would allow saving of PDF
            // to do so you will need to add a reference to Atalasoft.dotImage.Pdf.dll

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Tagged Image File Format (*.tif)|*.tif|Adobe PDF (*.pdf)|*.pdf";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (dlg.FilterIndex == 2)
                    {
                        // PDF
                        PdfEncoder pdfEnc = new PdfEncoder() { SizeMode = PdfPageSizeMode.FitToPage };
                        pdfEnc.SetEncoderCompression += pdfEnc_SetEncoderCompression;
                        this.documentAnnotationViewer1.Save(dlg.FileName, pdfEnc);
                    }
                    else
                    {
                        // TIFF
                        TiffEncoder tiffEnc = new TiffEncoder();
                        tiffEnc.SetEncoderCompression += tiffEnc_SetEncoderCompression;
                        this.documentAnnotationViewer1.Save(dlg.FileName, tiffEnc);
                    }
                }
            }
        }


        /// <summary>
        /// Intelligently select Tiff compression type
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void tiffEnc_SetEncoderCompression(object sender, EncoderCompressionEventArgs e)
        {
            switch (e.Image.PixelFormat)
            {
                case PixelFormat.Pixel1bppIndexed:
                    e.Compression = new TiffCodecCompression(TiffCompression.Group4FaxEncoding);
                    break;
                case PixelFormat.Pixel24bppBgr:
                case PixelFormat.Pixel8bppGrayscale:
                    e.Compression = new TiffCodecCompression(TiffCompression.JpegCompression);
                    break;
                default:
                    e.Compression = new TiffCodecCompression(TiffCompression.Lzw);
                    break;
            }
        }

        /// <summary>
        /// Intelligently set the compression used by a PdfEncoder
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void pdfEnc_SetEncoderCompression(object sender, EncoderCompressionEventArgs e)
        {
            switch (e.Image.PixelFormat)
            {
                case PixelFormat.Pixel1bppIndexed:
                    e.Compression = new PdfCodecCompression(PdfCompressionType.CcittGroup4);
                    break;
                case PixelFormat.Pixel24bppBgr:
                case PixelFormat.Pixel8bppGrayscale:
                    e.Compression = new PdfCodecCompression(PdfCompressionType.Jpeg);
                    break;
                default:
                    e.Compression = new PdfCodecCompression(PdfCompressionType.Deflate);
                    break;
            }
        }

        /// <summary>
        /// save as Tiff or Pdf with burned annotations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void fileBurnAndSaveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.FileName = "burned";
                dlg.Filter = "Tagged Image File Format (*.tif)|*.tif|Adobe PDF (*.pdf)|*.pdf";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    using (FileStream fs = new FileStream(dlg.FileName, FileMode.Create))
                    {
                        using (Atalasoft.Examples.StreamImageSource sis = new Atalasoft.Examples.StreamImageSource(BurnAnnotations()))
                        {
                            if (dlg.FilterIndex == 2)
                            {
                                // PDF
                                PdfEncoder pdfEnc = new PdfEncoder() { SizeMode = PdfPageSizeMode.FitToPage };
                                pdfEnc.SetEncoderCompression += pdfEnc_SetEncoderCompression;
                                pdfEnc.Save(fs, sis, null);

                            }
                            else
                            {
                                // TIFF
                                TiffEncoder tiffEnc = new TiffEncoder();
                                tiffEnc.SetEncoderCompression += tiffEnc_SetEncoderCompression;
                                tiffEnc.Save(fs, sis, null);
                            }
                        }
                    }
                }
            }
        }

        private void filePrintToolStripMenuItem_Click(object sender, EventArgs e)
        {

            AnnotationController printAc = new AnnotationController();
            MemoryStream printAnnosMs = new MemoryStream();
            int currentFrame = this.documentAnnotationViewer1.ThumbnailControl.SelectedIndicies[0];

            // Ask the user to select the printer
            using (PrintDialog printDialog = new PrintDialog())
            {
                printDialog.AllowCurrentPage = true;
                printDialog.AllowSelection = true;
                printDialog.AllowSomePages = true;
                printDialog.PrinterSettings.FromPage = 1;
                printDialog.PrinterSettings.ToPage = this.documentAnnotationViewer1.Count;

                
                if (printDialog.ShowDialog() == DialogResult.OK)
                {
                    switch (printDialog.PrinterSettings.PrintRange)
                    {
                        case System.Drawing.Printing.PrintRange.CurrentPage:
                            this._printMs = new MemoryStream();
                            // by saving to a single page format, we get only the current page
                            this.documentAnnotationViewer1.Save(this._printMs, new PngEncoder());
                            this._imgsrc = new Atalasoft.Examples.StreamImageSource(this._printMs);

                            // now for annotations
                            this.documentAnnotationViewer1.SaveAnnotationData(printAnnosMs, currentFrame, new XmpFormatter());
                            printAnnosMs.Seek(0, SeekOrigin.Begin);
                            
                            printAc.Load(printAnnosMs, AnnotationDataFormat.Xmp);
                          
                            break;
                        case System.Drawing.Printing.PrintRange.Selection:
                            // NOTE: THIS CODE IS ONLY CURRENTLY GOOD FOR SINGLE PAGE SELECTION MODE
                            // DO NOT ATTEMPT TO ENABLE MULTI PAGE SELCTION AND EXPECT PRINTING SELECTED TO WORK RIGHT
                            // build your printDoc and annotations from the set of selected pages (right now only one page)
                            

                            this._printMs = new MemoryStream();
                            // by saving to a single page format, we get only the current page
                            this.documentAnnotationViewer1.Save(this._printMs, new PngEncoder());
                            this._imgsrc = new Atalasoft.Examples.StreamImageSource(this._printMs);

                            // now for annotations
                            this.documentAnnotationViewer1.SaveAnnotationData(printAnnosMs, currentFrame, new XmpFormatter());
                            printAnnosMs.Seek(0, SeekOrigin.Begin);
                            
                            printAc.Load(printAnnosMs, AnnotationDataFormat.Xmp);
                            break;
                        case System.Drawing.Printing.PrintRange.SomePages:
                            // use the System.Drawing.Printing.FromPage and 
                            // System.Drawing.Printing.ToPage to make a subset of pages and annotations
                            this._printMs = new MemoryStream();
                            this.documentAnnotationViewer1.Save(this._printMs, new TiffEncoder());
                            this._imgsrc = new Atalasoft.Examples.RangedStreamImageSource(this._printMs, false, printDialog.PrinterSettings.FromPage -1, printDialog.PrinterSettings.ToPage -1);


                            // Now we need to fix up the annotations for the selected pages.. ugh
                            this.documentAnnotationViewer1.SaveAnnotationData(printAnnosMs, -1, new XmpFormatter());
                            printAnnosMs.Seek(0, SeekOrigin.Begin);
                            
                            printAc.Load(printAnnosMs, AnnotationDataFormat.Xmp);

                            // now that we have ALL annotations, we need to remove the ones outside range
                            // start by removing all from ToPage to end (if applicible)
                            //Console.WriteLine("PrintAC - Trimming end... ");
                            //Console.WriteLine("count: " + printAc.Layers.Count.ToString());
                            while (printAc.Layers.Count > printDialog.PrinterSettings.ToPage)
                            {
                                //Console.WriteLine("removing layer " + printDialog.PrinterSettings.ToPage.ToString());
                                printAc.Layers.RemoveAt(printDialog.PrinterSettings.ToPage);
                            }
                            //Console.WriteLine("PrintAC - Trimming start... ");
                            //Console.WriteLine(" Goal: " + (printDialog.PrinterSettings.ToPage - printDialog.PrinterSettings.FromPage).ToString());
                            //Console.WriteLine("count: " + printAc.Layers.Count.ToString());
                            while(printAc.Layers.Count > printDialog.PrinterSettings.ToPage - printDialog.PrinterSettings.FromPage + 1)
                            {
                                printAc.Layers.RemoveAt(0);
                            }

                            Console.WriteLine("  finalCount: " + printAc.Layers.Count.ToString());

                            //for (int i = printDialog.PrinterSettings.ToPage - 1; i < printAc.Layers.Count; i++)
                            //{
                            //    printAc.Layers.RemoveAt(printDialog.PrinterSettings.ToPage)
                            //}

                            break;
                        case System.Drawing.Printing.PrintRange.AllPages:
                        default:
                            // your annotations and document will consist of all pages
                            // Prepare the document (images) for printing
                            this._printMs = new MemoryStream();
                            this.documentAnnotationViewer1.Save(this._printMs, new TiffEncoder());
                            this._imgsrc = new Atalasoft.Examples.StreamImageSource(this._printMs);
                            // NOTE: the pDoc_EndPrint event will take care of clearing this up

                            // Prepare the annotations
                            
                            this.documentAnnotationViewer1.SaveAnnotationData(printAnnosMs, -1, new XmpFormatter());
                            printAnnosMs.Seek(0, SeekOrigin.Begin);
                            
                            printAc.Load(printAnnosMs, AnnotationDataFormat.Xmp);
                            // once loaded, the memorystream for annotations aren't needed anymore

                            break;
                    }

                    printAnnosMs.Close();
                    printAnnosMs.Dispose();

                    AnnotatePrintDocument pDoc = new AnnotatePrintDocument();
                    pDoc.Annotations = printAc;
                    printDialog.Document = pDoc;

                    // This is where we tell it how to acquire the next image
                    pDoc.GetImage += new PrintImageEventHandler(pDoc_GetImage);
                    // This is where to tell it to release each page once the page is done
                    pDoc.AfterPrintPage += new PrintImageEventHandler(pDoc_AfterPrintPage);
                    pDoc.EndPrint += new System.Drawing.Printing.PrintEventHandler(pDoc_EndPrint);

                    // get as much of the image on the page as possible
                    pDoc.ScaleMode = PrintScaleMode.FitToEdges;
                    pDoc.Center = true;

                    pDoc.Units = AnnotationUnit.Pixel;

                    // Execute the print job
                    pDoc.Print();
                }
            }
        }

        private void fileExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._device.State == TwainState.SourceOpen)
            {
                this._device.Close();
            }
            this.Close();
        }

        #endregion File Menu Events

        #region View Menu Events
        private void viewBestFitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.BestFit;
        }

        private void viewFitToWidthToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.FitToWidth;
        }

        private void viewZoom200ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // need to turn off auto zoom so manual zoom can "take"
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.None;
            this.documentAnnotationViewer1.ImageControl.Zoom = 2.0f;
        }
        private void viewZoom100ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // need to turn off auto zoom so manual zoom can "take"
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.None;
            this.documentAnnotationViewer1.ImageControl.Zoom = 1.0f;
        }
        private void viewZoom75ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // need to turn off auto zoom so manual zoom can "take"
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.None;
            this.documentAnnotationViewer1.ImageControl.Zoom = 0.75f;
        }

        private void viewZoom50ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // need to turn off auto zoom so manual zoom can "take"
            this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.None;
            this.documentAnnotationViewer1.ImageControl.Zoom = 0.5f;
        }
        #endregion View Menu Events

        #region Annotation Menu Events
        private void annotationsAddRectangleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Modifying the default rectangle annotation
            RectangleAnnotation rect = new RectangleAnnotation();
            rect.Fill = new AnnotationBrush(Color.Orange);
            rect.Outline = new AnnotationPen(Color.Pink, 5);

            // Let the user interactively draw the rectangle
            documentAnnotationViewer1.Annotations.CreateAnnotation(rect);
        }
        private void annotationsAddEllipseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Modifying the default Ellipse annotation
            EllipseAnnotation ellipse = new EllipseAnnotation();
            ellipse.Fill = new AnnotationBrush(Color.Azure);
            ellipse.Outline = new AnnotationPen(Color.Blue, 5);

            // Let the user interactively draw the Ellipse
            documentAnnotationViewer1.Annotations.CreateAnnotation(ellipse);
        }

        private void annotationsAddTextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextAnnotation text = new TextAnnotation();
            text.Fill = new AnnotationBrush(Color.Ivory);
            text.Outline = new AnnotationPen(Color.Red, 5);
            text.Font = new AnnotationFont("Courier New", 24.0f);
            text.Text = "Text Annotation Here";

            // This code added to address an issue where text changes were not persisting
            text.PropertyChanged += text_PropertyChanged;

            documentAnnotationViewer1.Annotations.CreateAnnotation(text);
        }

        // Event fires when many different properties of the annotation have changed
        // we are after the Text property so we can force the thumbnail viewer to update
        void text_PropertyChanged(object sender, AnnotationPropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Text")
            {
                this.documentAnnotationViewer1.RefreshCurrentImageThumbnail();
            }
        }


        private void annotationsDeleteSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.Annotations.SelectedAnnotations != null && this.documentAnnotationViewer1.Annotations.SelectedAnnotations.Length > 0)
            {
                this.documentAnnotationViewer1.Annotations.CurrentLayer.Items.Remove(this.documentAnnotationViewer1.Annotations.SelectedAnnotations);
            }
        }

        private void annotationsBurnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("This Action Can not be undone\nAre you sure you want to do this?", "WARNING", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                documentAnnotationViewer1.Annotations.Layers.Clear();
                documentAnnotationViewer1.Open(BurnAnnotations());
            }
        }

        private void annotationsSaveXmpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.FileName = "annotations.xmp";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    using (FileStream fs = new FileStream(dlg.FileName, FileMode.Create))
                    {
                        this.documentAnnotationViewer1.SaveAnnotationData(fs, -1, new XmpFormatter());
                    }
                }
            }
        }
        #endregion Annotation Menu Events
 
        #region TWAIN Menu Events
        private void twainSelectSourceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this._device = this._acquisition.ShowSelectSource();
        }

        private void twainAcquireToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._acquisition.Devices.Default != null)
            {
                this._device = this._acquisition.Devices.Default;
            }
            if (this._device.TryOpen())
            {
                // set desired settings here Just setting up a 200 DPI bitonal scan for now
                this._device.AutoDiscardBlankPages = AutoDiscardMode.Disabled;
                this._device.ModalAcquire = true;
                this._device.PixelType = ImagePixelType.BlackAndWhite;
                this._device.BitDepth = 1;
                this._device.Resolution = new TwainResolution(200f, 200f, UnitType.Pixels);

                // this kicks offf the scan.. the system TWAIN manager is now in charge...
                // ImageAcquiredEvents will fire until the scan is finished.
                this._device.Acquire();
            }
        }

        private void twainAcquireNoUiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._acquisition.Devices.Default != null)
            {
                this._device = this._acquisition.Devices.Default;
            }
            if (this._device.TryOpen())
            {
                // set desired settings here Just setting up a 200 DPI bitonal scan for now
                this._device.AutoDiscardBlankPages = AutoDiscardMode.Disabled;
                this._device.ModalAcquire = true;
                //this._device.PixelType = ImagePixelType.BlackAndWhite;
                //this._device.BitDepth = 1;

                this._device.PixelType = ImagePixelType.Color;
                this._device.BitDepth = 24;

                this._device.Resolution = new TwainResolution(200f, 200f, UnitType.Pixels);

                this._device.HideInterface = true;

                // this kicks offf the scan.. the system TWAIN manager is now in charge...
                // ImageAcquiredEvents will fire until the scan is finished.
                this._device.Acquire();
            }
        }

        private void acquireNoUi200GrayscaleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._acquisition.Devices.Default != null)
            {
                this._device = this._acquisition.Devices.Default;
            }
            if (this._device.TryOpen())
            {
                // set desired settings here Just setting up a 200 DPI bitonal scan for now
                this._device.AutoDiscardBlankPages = AutoDiscardMode.Disabled;
                this._device.ModalAcquire = true;
                //this._device.PixelType = ImagePixelType.BlackAndWhite;
                //this._device.BitDepth = 1;
                this._device.DuplexEnabled = true;

                this._device.PixelType = ImagePixelType.Grayscale;
                this._device.BitDepth = 8;

                this._device.Resolution = new TwainResolution(200f, 200f, UnitType.Pixels);

                this._device.HideInterface = true;

                // this kicks offf the scan.. the system TWAIN manager is now in charge...
                // ImageAcquiredEvents will fire until the scan is finished.
                this._device.Acquire();
            }
        }

        private void acquireSimplexNoIo200GrayscaleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this._acquisition.Devices.Default != null)
            {
                this._device = this._acquisition.Devices.Default;
            }
            if (this._device.TryOpen())
            {
                // set desired settings here Just setting up a 200 DPI bitonal scan for now
                this._device.AutoDiscardBlankPages = AutoDiscardMode.Disabled;
                this._device.ModalAcquire = true;
                //this._device.PixelType = ImagePixelType.BlackAndWhite;
                //this._device.BitDepth = 1;
                this._device.DuplexEnabled = false;

                this._device.PixelType = ImagePixelType.Grayscale;
                this._device.BitDepth = 8;

                this._device.Resolution = new TwainResolution(200f, 200f, UnitType.Pixels);

                this._device.HideInterface = true;

                // this kicks offf the scan.. the system TWAIN manager is now in charge...
                // ImageAcquiredEvents will fire until the scan is finished.
                this._device.Acquire();
            }
        }
        #endregion TWAIN Menu Events

        #region Processing Menu Events
        private void processingRotateRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.RotateDocument(DocumentRotation.Rotate90);
        }

        private void processingRotateLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.RotateDocument(DocumentRotation.Rotate270);
        }

        private void processingRotate180ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.RotateDocument(DocumentRotation.Rotate180);
        }

        private void processingFlipHorizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FlipCommand flip = new FlipCommand(FlipDirection.Horizontal);
            this.documentAnnotationViewer1.ApplyCommand(flip);
        }

        private void processingFlipVerticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FlipCommand flip = new FlipCommand(FlipDirection.Vertical);
            this.documentAnnotationViewer1.ApplyCommand(flip);
        }
        
        private void processingDeleteSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(this.documentAnnotationViewer1.ThumbnailControl.SelectedIndicies != null && this.documentAnnotationViewer1.ThumbnailControl.SelectedIndicies.Length > 0)
            {
                this.documentAnnotationViewer1.RemoveSelected();
            }
            else
            {
                MessageBox.Show("Select one or more frames in the ThumbnailView to delete and try again...");
            }
        }


        private void processingSelectForCropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.ImageControl.Image != null)
            {
                this.documentAnnotationViewer1.ImageControl.MouseTool = MouseToolType.Selection;
            }
        }

        private void processingCropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.ImageControl.Selection != null && this.documentAnnotationViewer1.ImageControl.Image != null)
            {
                CropCommand crop = new CropCommand(this.documentAnnotationViewer1.ImageControl.Selection.Bounds);
                this.documentAnnotationViewer1.ApplyCommand(crop);
            }
        }

        private void processingQuickCropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.ClearSelection();
            this.documentAnnotationViewer1.ImageControl.MouseTool = MouseToolType.Selection;
            this.documentAnnotationViewer1.ImageControl.Selection.Changed += Selection_Changed;
        }


        void Selection_Changed(object sender, RubberbandEventArgs e)
        {
            this.documentAnnotationViewer1.ImageControl.MouseTool = MouseToolType.None;
            if (this.documentAnnotationViewer1.ImageControl.Selection != null)
            {
                this.documentAnnotationViewer1.ImageControl.Selection.Changed -= Selection_Changed;
                CropCommand crop = new CropCommand(this.documentAnnotationViewer1.ImageControl.Selection.Bounds);
                AtalaImage cropped = crop.Apply(this.documentAnnotationViewer1.ImageControl.Image).Image;

                string currentImgCap = "UNKNOWN";
                if (this.documentAnnotationViewer1.ThumbnailControl.SelectedItems.Count > 0)
                {
                    currentImgCap = this.documentAnnotationViewer1.ThumbnailControl.SelectedItems[0].Text;
                }

                this.documentAnnotationViewer1.Add(cropped, "CROP_FROM_" + this.documentAnnotationViewer1.CurrentImageIndex.ToString("D3"), "");

                this.documentAnnotationViewer1.ImageControl.Selection.Visible = false;
            }
        }

        private void processingClearCropSelctionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.documentAnnotationViewer1.ImageControl.MouseTool = MouseToolType.None;
            this.documentAnnotationViewer1.ImageControl.Selection.Visible = false;
        }

        private void processingChangePixelFormat8bppGrayscaleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.ImageControl.Image != null && this.documentAnnotationViewer1.ImageControl.Image.PixelFormat != PixelFormat.Pixel8bppGrayscale)
            {
                ChangePixelFormatCommand cmd = new ChangePixelFormatCommand(PixelFormat.Pixel8bppGrayscale);
                this.documentAnnotationViewer1.ApplyCommand(cmd);
            }
        }

        private void processingChangePixelFormat24bppBgrToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.ImageControl.Image != null && this.documentAnnotationViewer1.ImageControl.Image.PixelFormat != PixelFormat.Pixel24bppBgr)
            {
                ChangePixelFormatCommand cmd = new ChangePixelFormatCommand(PixelFormat.Pixel24bppBgr);
                this.documentAnnotationViewer1.ApplyCommand(cmd);
            }
        }

        private void processingChangePixelFormat1bppIndexedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.documentAnnotationViewer1.ImageControl.Image != null && this.documentAnnotationViewer1.ImageControl.Image.PixelFormat != PixelFormat.Pixel1bppIndexed)
            {
                GlobalThresholdCommand cmd = new GlobalThresholdCommand();
                this.documentAnnotationViewer1.ApplyCommand(cmd);
            }
        }


        #endregion Processing Menu Events

        #region Print Events
        void pDoc_AfterPrintPage(object sender, PrintImageEventArgs e)
        {
            this._imgsrc.Release(e.Image);
        }

        void pDoc_GetImage(object sender, PrintImageEventArgs e)
        {
            if (_imgsrc.HasMoreImages())
            {
                e.Image = _imgsrc.AcquireNext();
                e.HasMorePages = _imgsrc.HasMoreImages();
            }
        }

        void pDoc_EndPrint(object sender, System.Drawing.Printing.PrintEventArgs e)
        {
            if (this._imgsrc != null)
            {
                this._imgsrc.Dispose();
            }
            if (this._printMs != null)
            {
                this._printMs.Close();
                this._printMs.Dispose();
            }
        }
        #endregion Print Events

        #region TWAIN Scanning Events
        /// <summary>
        /// This event is fired in situations where the scanner driver throws an exception
        /// while in the middle of the scanning process... since the scanning process happens
        /// asynchronously, this is where things like paper jams and loss of connection (after
        /// connection was previously established) will show up
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _acquisition_AsynchronousException(object sender, AsynchronousExceptionEventArgs e)
        {
            // Try to gracefully close the device so we don't end up "keeping our thumb on the scanner"
            if (this._device.State == TwainState.SourceOpen)
            {
                this._device.Close();
            }
            // in this simple example we're just blabbing the message.. 
            // production code would likely want to provide user friendly message and probably log the error 
            // message and StackTrace for use in troubleshooting
            MessageBox.Show("ERROR: " + e.Exception.Message);
        }

        /// <summary>
        /// In this example, we're not doing any "heavy processing" of images after scanning
        /// so we just need to gracefully ensure the device is closed
        /// 
        /// However, if you need to process images that wasere scanned in some bulk way -this 
        /// is the time to do it as the scan operation has completed... no more images are coming in
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _acquisition_AcquireFinished(object sender, EventArgs e)
        {
            // there is no processing needed on finished but if you did need to take some action 
            // after a batch acquire finished, this is the place to do it
            if (this._device.State == TwainState.SourceOpen)
            {
                this._device.Close();
            }
        }

        void _acquisition_AcquireCanceled(object sender, EventArgs e)
        {
            // Again, we are just making sure we gracefully close the device so as not to cause 
            // errors where device is blocked because it's been left hanging
            if (this._device.State == TwainState.SourceOpen)
            {
                this._device.Close();
            }
        }

        /// <summary>
        /// This event fires for each individual image that is acquired...
        /// It's important to try and keep overhead to a minimum... use the AcquireFinished event
        /// for situations where you need to do bulk processing on the scanned images
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _acquisition_ImageAcquired(object sender, AcquireEventArgs e)
        {
            //// Original code was simple but some scanners axuire grayscale as 8bppIndexed
            //// when this happens, it makes the images not jpeg compressable
            //// this is a fix for it
            //if (e.Image != null)
            //{
            //    // we just add the image
            //    this.documentAnnotationViewer1.Add(AtalaImage.FromBitmap(e.Image), null, null);
            //}

            // scanners sometimes provide 8bppIndexed images when we meant grayscale
            // to counter this we will convert before adding here

            AtalaImage imgToAdd = null;
            AtalaImage acquiredImg = AtalaImage.FromBitmap(e.Image);

            // Convert 8bppIndexed to grayscale
            if (acquiredImg.PixelFormat == PixelFormat.Pixel8bppIndexed)
            {
                imgToAdd = acquiredImg.GetChangedPixelFormat(PixelFormat.Pixel8bppGrayscale);
                acquiredImg.Dispose();
            }
            else
            {
                imgToAdd = acquiredImg;
            }

            this.documentAnnotationViewer1.Add(imgToAdd, null, null);
        }
        #endregion TWAIN Scanning Events

        #region Utility
        /// <summary>
        /// This enables embedding of xmp annotations in TIFF files
        /// </summary>
        /// <param name="viewer"></param>
        /// <param name="options"></param>
        private void annotationSaveOptionsHandler(DocumentAnnotationViewer viewer, AnnotationSaveOptions options)
        {
            options.TiffAnnotationFormat = AnnotationDataFormat.Xmp;
            options.EmbedAnnotations = true;
        }
        
        /// <summary>
        /// Gives you direct control to modify annotations before exporting and to control how the PDfAnnotationDataExporter will treat existing annotations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private PdfAnnotationDataExporter documentAnnotationViewer1_CreatePdfAnnotationDataExporter(object sender, CreatePdfAnnotationDataExporterEventArgs e)
        {
            PdfAnnotationDataExporter exp = new PdfAnnotationDataExporter();
            exp.AlwaysEmbedAnnotationData = true;
            exp.OverwriteExistingAnnotations = true;
            return exp;
        }

        private MemoryStream BurnAnnotations()
        {
            MemoryStream saveStream = new MemoryStream();

            MemoryStream docStream = new MemoryStream();
            this.documentAnnotationViewer1.Save(docStream, new TiffEncoder());
            docStream.Seek(0, SeekOrigin.Begin);
            Atalasoft.Examples.StreamImageSource sourceImageSource = new Atalasoft.Examples.StreamImageSource(docStream);

            // Prepare the annotations
            MemoryStream burnAnnosMs = new MemoryStream();
            this.documentAnnotationViewer1.SaveAnnotationData(burnAnnosMs, -1, new XmpFormatter());
            burnAnnosMs.Seek(0, SeekOrigin.Begin);
            AnnotationController burnAc = new AnnotationController();
            burnAc.Load(burnAnnosMs, AnnotationDataFormat.Xmp);

            // once loaded, the memorystream for annotations aren't needed anymore
            burnAnnosMs.Close();
            burnAnnosMs.Dispose();

            TiffEncoder encoder = new TiffEncoder();
            encoder.Append = false;

            while (sourceImageSource.HasMoreImages())
            {
                AtalaImage rawImage = sourceImageSource.AcquireNext();
                AtalaImage workingImage = rawImage;


                // do the burn if there are annotations to burn
                if (burnAc.Layers.Count > (sourceImageSource.Current - 1) && burnAc.Layers[sourceImageSource.Current - 1] != null && burnAc.Layers[sourceImageSource.Current - 1].Items.Count > 0)
                {
                    // for burning annotatins, we really ned 24 bit per pixel color as annotations have color
                    if (rawImage.PixelFormat != PixelFormat.Pixel24bppBgr)
                    {
                        workingImage = rawImage.GetChangedPixelFormat(PixelFormat.Pixel24bppBgr);
                    }

                    burnAc.RenderAnnotations(new RenderDevice(), workingImage.GetGraphics(), burnAc.Layers[sourceImageSource.Current - 1]);
                }
                workingImage.Save(saveStream, encoder, null);
                if (!AtalaImage.ReferenceEquals(rawImage, workingImage))
                {
                    workingImage.Dispose();
                }

                encoder.Append = true;
                sourceImageSource.Release(rawImage);
            }

            saveStream.Seek(0, SeekOrigin.Begin);
            return saveStream;
        }

        private void documentAnnotationViewer1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (documentAnnotationViewer1.ImageControl.Image != null)
            {
                toolStripStatusLabel1.Text = "Frame: " + documentAnnotationViewer1.CurrentImageIndex.ToString() + " - " + documentAnnotationViewer1.ImageControl.Image.Size.ToString() + " - " + documentAnnotationViewer1.ImageControl.Image.PixelFormat.ToString();
            }
            else
            {
                toolStripStatusLabel1.Text = "Status: Ready";
            }
        }
        #endregion Utility

        #region Horizontal Scroll Stuff
        /// <summary>
        /// This is going to be ONLY for horizontal scrolling
        /// we have had to carefully enable.disable native mouse wheel scrolling in the viewer becasue that scrolling will
        /// override/mess with horizontal if its on
        /// 
        /// so we have disabled the built in mouse wheel scrolling and now we can manually horizontally scroll
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            // we could maybe be more elegant.. 
            // Unfortunately, there's no documentAnnotationViewer1.Iamgecontrol.Focused property 
            // so if we wanted to be more tight with this, we'd have to get the area of the image control
            // and make our own decision about whether the mouse was inside or outside of it
            // still it works
            if (this._shift)
            {
                int oldX = this.documentAnnotationViewer1.ImageControl.ScrollPosition.X;
                int oldY = this.documentAnnotationViewer1.ImageControl.ScrollPosition.Y;

                // Ok, we've got the old values - calculate the new X
                int newX = oldX + e.Delta;

                // this is the "safety dance" to prevent us from scrolling beyond the image
                if (newX > 0)
                {
                    newX = 0;
                }
                else if (newX > this.documentAnnotationViewer1.ImageControl.Image.Width)
                {
                    newX = this.documentAnnotationViewer1.ImageControl.Image.Width;
                }

                // finally we set the new X (and keep the old Y as we are horizontal scrolling only)
                this.documentAnnotationViewer1.ImageControl.ScrollPosition = new Point(newX, oldY);
            }
            if (this._ctrl)
            {
                if (this.documentAnnotationViewer1.ImageControl.AutoZoom != AutoZoomMode.None)
                {
                    this.documentAnnotationViewer1.ImageControl.AutoZoom = AutoZoomMode.None;
                }

                int delta = e.Delta;
                double modifier = 1;
                if (e.Delta > 0)
                {
                    modifier = Math.Abs((double)e.Delta / (double)100);
                }
                else
                {
                    modifier = Math.Abs((double)100 / (double)e.Delta);
                }

                if (modifier == 0)
                {
                    modifier = 1.0;
                }
                this.documentAnnotationViewer1.ImageControl.Zoom = (this.documentAnnotationViewer1.ImageControl.Zoom * modifier);
            }
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            if (!e.Shift)
            {
                //Console.WriteLine("SHIFT UP!");
                this._shift = false;
                // we need to toggle the mouse wheel scrolling of the image control on
                // when shift isn't being pressed
                this.documentAnnotationViewer1.ImageControl.MouseWheelScrolling = true;
            }
            if (!e.Control)
            {
                this._ctrl = false;
                this.documentAnnotationViewer1.ImageControl.MouseWheelScrolling = true;
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Shift)
            {
                //Console.WriteLine("SHIFT DOWN!");
                this._shift = true;
                // need this to prevent our horizontal scrolling from interfering with defualt
                this.documentAnnotationViewer1.ImageControl.MouseWheelScrolling = false;
            }
            if (e.Control)
            {
                this._ctrl = true;
                this.documentAnnotationViewer1.ImageControl.MouseWheelScrolling = false;
            }
        }















        #endregion Horizontal Scroll Stuff

    }
}
