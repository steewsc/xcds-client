/*
 * Xibo - Digitial Signage - http://www.xibo.org.uk
 * Copyright (C) 2006,2007,2008 Daniel Garner and James Packer
 *
 * This file is part of Xibo.
 *
 * Xibo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * any later version. 
 *
 * Xibo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with Xibo.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace XiboClient
{
    class Text : Media
    {
        private string filePath;
        private string direction;
        private string backgroundImage;
        private string backgroundColor;
        private WebBrowser webBrowser;
        private string _documentText;
        private String _headText;

        private string backgroundTop;
        private string backgroundLeft;
        private double _scaleFactor;
        private int _scrollSpeed;

        private TemporaryHtml _tempHtml;

        //<summary>
        //Creates a Text display control
        //</summary>
        //<param name="width">The Width of the Panel</param>
        public Text(RegionOptions options)
            : base(options.width, options.height, options.top, options.left)
        {
            this.filePath = options.uri;
            this.direction = options.direction;
            this.backgroundImage = options.backgroundImage;
            this.backgroundColor = options.backgroundColor;
            
            _scaleFactor = options.scaleFactor;

            backgroundTop = options.backgroundTop + "px";
            backgroundLeft = options.backgroundLeft + "px";

            // Generate a temporary file to store the rendered object in.
            _tempHtml = new TemporaryHtml();


            // Set the text
            _documentText = options.text;
            _scrollSpeed = options.scrollSpeed;
           
            // What do we want the background to look like
            String bodyStyle;
            
            if (backgroundImage == null || backgroundImage == "")
            {
                bodyStyle = "background-color:" + backgroundColor + " ;";
            }
            else
            {
                bodyStyle = "background-image: url('" + backgroundImage + "'); background-attachment:fixed; background-color:" + backgroundColor + " background-repeat: no-repeat; background-position: " + backgroundLeft + " " + backgroundTop + ";";
            }

            _headText = String.Format("{1}<style type='text/css'>body {{{2}}}, p, h1, h2, h3, h4, h5 {{ margin:2px; font-size:{0}em; }}</style>", options.scaleFactor.ToString(), options.javaScript, bodyStyle);

            // Store the document text in the temporary HTML space
            _tempHtml.HeadContent = _headText;

            // Generate the Body
            if (direction == "none")
            {
                // Just use the RAW text that was in the XLF
                _tempHtml.BodyContent = _documentText;
            }
            else
            {
                // Format the text in some way
                String textRender = "";
                String textWrap = "";

                if (direction == "left" || direction == "right") textWrap = "white-space: nowrap";

                textRender += string.Format("<div id='text' style='position:relative;overflow:hidden;width:{0}; height:{1};'>", this.width - 10, this.height);
                textRender += string.Format("<div id='innerText' style='position:absolute; left: 0px; top: 0px; {0}'>{1}</div></div>", textWrap, _documentText);

                _tempHtml.BodyContent = textRender;
            }

            // Fire up a webBrowser control
            webBrowser = new WebBrowser();
            webBrowser.Size = this.Size;
            webBrowser.ScrollBarsEnabled = false;
            webBrowser.Navigate(_tempHtml.Path);
            webBrowser.DocumentCompleted += new WebBrowserDocumentCompletedEventHandler(webBrowser_DocumentCompleted);
        }

        void webBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            HtmlDocument htmlDoc = webBrowser.Document;

            // decide whether we need a marquee or not
            if (direction == "none")
            {
                //we dont
                //set the body of the webBrowser to the document text
                htmlDoc.Body.InnerHtml = _documentText;
            }
            else
            {
                String textRender = "";
                String textWrap = "";
                if (direction == "left" || direction == "right") textWrap = "white-space: nowrap";

                textRender += string.Format("<div id='text' style='position:relative;overflow:hidden;width:{0}; height:{1};'>", this.width - 10, this.height);
                textRender += string.Format("<div id='innerText' style='position:absolute; left: 0px; top: 0px; {0}'>{1}</div></div>", textWrap, _documentText);

                htmlDoc.Body.InnerHtml = textRender;

                Object[] objArray = new Object[2];
                objArray[0] = direction;
                objArray[1] = _scrollSpeed;

                htmlDoc.InvokeScript("init", objArray);
            }

            System.Diagnostics.Debug.WriteLine(htmlDoc.Body.InnerHtml, LogType.Audit.ToString());

            // Try to call the EmbedInit Function
            try
            {
                htmlDoc.InvokeScript("EmbedInit");
            }
            catch { }

            // Add the control
            this.Controls.Add(webBrowser);

            Show();
        }

        public override void RenderMedia()
        {
            base.StartTimer();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    webBrowser.DocumentText = "";
                    webBrowser.Dispose();
                }
                catch
                {
                    System.Diagnostics.Trace.WriteLine(new LogMessage("WebBrowser still in use.", String.Format("Dispose")));
                }
            }

            base.Dispose(disposing);
        }
    }
}
