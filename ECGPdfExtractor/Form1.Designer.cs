namespace ECGPdfExtractor
{
    partial class Form1
    {
        /// <summary>
        /// 設計工具所需的變數。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清除任何使用中的資源。
        /// </summary>
        /// <param name="disposing">如果應該處置受控資源則為 true，否則為 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 設計工具產生的程式碼

        /// <summary>
        /// 此為設計工具支援所需的方法 - 請勿使用程式碼編輯器修改
        /// 這個方法的內容。
        /// </summary>
        private void InitializeComponent()
        {
            this.tbMsg = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btConvert = new System.Windows.Forms.Button();
            this.cbDelete = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // tbMsg
            // 
            this.tbMsg.Location = new System.Drawing.Point(22, 71);
            this.tbMsg.Multiline = true;
            this.tbMsg.Name = "tbMsg";
            this.tbMsg.Size = new System.Drawing.Size(628, 339);
            this.tbMsg.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(29, 30);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(158, 12);
            this.label1.TabIndex = 2;
            this.label1.Text = "Select a directory to extract PDFs";
            // 
            // btConvert
            // 
            this.btConvert.Location = new System.Drawing.Point(223, 25);
            this.btConvert.Name = "btConvert";
            this.btConvert.Size = new System.Drawing.Size(93, 23);
            this.btConvert.TabIndex = 3;
            this.btConvert.Text = "Open PDFs";
            this.btConvert.UseVisualStyleBackColor = true;
            this.btConvert.Click += new System.EventHandler(this.btConvert_Click);
            // 
            // cbDelete
            // 
            this.cbDelete.AutoSize = true;
            this.cbDelete.Checked = true;
            this.cbDelete.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbDelete.Location = new System.Drawing.Point(404, 29);
            this.cbDelete.Name = "cbDelete";
            this.cbDelete.Size = new System.Drawing.Size(126, 16);
            this.cbDelete.TabIndex = 4;
            this.cbDelete.Text = "Delete temporary files";
            this.cbDelete.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(680, 431);
            this.Controls.Add(this.cbDelete);
            this.Controls.Add(this.btConvert);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.tbMsg);
            this.Name = "Form1";
            this.Text = "Extract Data from MUSE ECG PDF";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.TextBox tbMsg;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btConvert;
        private System.Windows.Forms.CheckBox cbDelete;
    }
}

