namespace CurveTester {
    partial class MainWindow {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose (bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent () {
            this.CurveMode = new System.Windows.Forms.ComboBox();
            this.Tension = new System.Windows.Forms.TrackBar();
            ((System.ComponentModel.ISupportInitialize)(this.Tension)).BeginInit();
            this.SuspendLayout();
            // 
            // CurveMode
            // 
            this.CurveMode.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.CurveMode.BackColor = System.Drawing.Color.Black;
            this.CurveMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CurveMode.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.CurveMode.ForeColor = System.Drawing.Color.White;
            this.CurveMode.FormattingEnabled = true;
            this.CurveMode.Items.AddRange(new object[] {
            "Hermite",
            "Catmull-Rom",
            "Cardinal"});
            this.CurveMode.Location = new System.Drawing.Point(12, 276);
            this.CurveMode.Name = "CurveMode";
            this.CurveMode.Size = new System.Drawing.Size(200, 24);
            this.CurveMode.TabIndex = 0;
            this.CurveMode.SelectedIndexChanged += new System.EventHandler(this.CurveMode_SelectedIndexChanged);
            // 
            // Tension
            // 
            this.Tension.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Tension.AutoSize = false;
            this.Tension.LargeChange = 10;
            this.Tension.Location = new System.Drawing.Point(218, 276);
            this.Tension.Maximum = 100;
            this.Tension.Name = "Tension";
            this.Tension.Size = new System.Drawing.Size(169, 24);
            this.Tension.TabIndex = 1;
            this.Tension.TickFrequency = 10;
            this.Tension.TickStyle = System.Windows.Forms.TickStyle.None;
            this.Tension.ValueChanged += new System.EventHandler(this.Tension_ValueChanged);
            // 
            // MainWindow
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(399, 312);
            this.Controls.Add(this.Tension);
            this.Controls.Add(this.CurveMode);
            this.Font = new System.Drawing.Font("Tahoma", 10F);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "MainWindow";
            this.Text = "Curve Tester";
            ((System.ComponentModel.ISupportInitialize)(this.Tension)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox CurveMode;
        private System.Windows.Forms.TrackBar Tension;
    }
}