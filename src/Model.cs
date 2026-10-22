using System;
using System.Collections.Generic;

namespace DangAn
{
    public class Field
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Source { get; set; }
        public Field() { Name = ""; Value = ""; Source = ""; }
        public Field(string n, string v, string s) { Name = n ?? ""; Value = v ?? ""; Source = s ?? ""; }
    }

    public class Contract
    {
        public string Kind { get; set; }
        public string Party { get; set; }
        public double Amount { get; set; }
        public string Date { get; set; }
        public double Paid { get; set; }
        public double Unpaid { get; set; }
        public string Final { get; set; }
        public string Content { get; set; }
        public string Note { get; set; }
        public string Source { get; set; }
        public Contract() { Kind = ""; Party = ""; Date = ""; Final = ""; Content = ""; Note = ""; Source = ""; }
    }

    /// <summary>一笔收支：可关联记账凭证、发票原件和合同。金额单位：元。</summary>
    public class Payment
    {
        public string Date { get; set; }
        public string Dir { get; set; }          // 支出 / 收入
        public string Summary { get; set; }
        public string Party { get; set; }
        public double Amount { get; set; }
        public string VoucherNo { get; set; }
        public string VoucherFile { get; set; }  // 相对档案根目录
        public string InvoiceFile { get; set; }
        public string Contract { get; set; }     // 关联合同（类型）
        public string Source { get; set; }       // 来源明细表，或“手工”
        public string Note { get; set; }
        public Payment() { Date = ""; Dir = "支出"; Summary = ""; Party = ""; VoucherNo = ""; VoucherFile = ""; InvoiceFile = ""; Contract = ""; Source = "手工"; Note = ""; }
    }

    /// <summary>从合同、凭证等混合 PDF 中拆出的单张发票。</summary>
    public class InvoiceRec
    {
        public string Rel { get; set; }       // 拆出的发票 PDF（相对档案根目录）
        public string Source { get; set; }    // 来源文件
        public int Page { get; set; }         // 来源第几页（从 1 起）
        public double Amount { get; set; }    // 价税合计
        public string Date { get; set; }
        public string Seller { get; set; }
        public string Number { get; set; }
        public string Project { get; set; }
        /// <summary>“发票合集”包含的单张发票（单张发票此项为空）。</summary>
        public List<string> Members { get; set; }
        public InvoiceRec() { Rel = ""; Source = ""; Date = ""; Seller = ""; Number = ""; Project = ""; Members = new List<string>(); }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public string Stage { get; set; }
        public string Note { get; set; }
        public List<string> Folders { get; set; }
        public List<Field> Fields { get; set; }
        public List<Contract> Contracts { get; set; }
        public List<string> Required { get; set; }
        public List<Payment> Payments { get; set; }
        public Project()
        {
            Id = ""; Name = ""; Code = ""; Type = ""; Stage = ""; Note = "";
            Folders = new List<string>(); Fields = new List<Field>();
            Contracts = new List<Contract>(); Required = new List<string>(); Payments = new List<Payment>();
        }
        public string Get(string field)
        {
            foreach (var f in Fields) if (f.Name == field) return f.Value ?? "";
            return "";
        }
        public void Set(string field, string value, string source)
        {
            foreach (var f in Fields) if (f.Name == field) { f.Value = value ?? ""; if (source != null) f.Source = source; return; }
            Fields.Add(new Field(field, value, source));
        }
        public bool IsRoot { get { return Id == Store.RootProjectId; } }
    }

    public class FileRec
    {
        public string Rel { get; set; }
        public long Size { get; set; }
        public string Time { get; set; }
        public string Project { get; set; }
        public string Cat { get; set; }
        public FileRec() { Rel = ""; Time = ""; Project = ""; Cat = ""; }
        public string Name { get { int i = Rel.LastIndexOf('\\'); return i < 0 ? Rel : Rel.Substring(i + 1); } }
        public string Ext { get { int i = Rel.LastIndexOf('.'); return i < 0 ? "" : Rel.Substring(i).ToLowerInvariant(); } }
        public string Dir { get { int i = Rel.LastIndexOf('\\'); return i < 0 ? "" : Rel.Substring(0, i); } }
    }

    public class Library
    {
        public int Version { get; set; }
        public string Root { get; set; }
        public string Updated { get; set; }
        public string BackupDir { get; set; }
        public bool AutoBackup { get; set; }
        public List<string> Excludes { get; set; }
        public List<Project> Projects { get; set; }
        public List<FileRec> Files { get; set; }
        public Dictionary<string, string> FileProject { get; set; }
        public Dictionary<string, string> FileCat { get; set; }
        public List<string> FillFields { get; set; }
        public Dictionary<string, string> TextTried { get; set; }
        /// <summary>共用资料：一份文件除归属项目外，还同时显示在这些项目里（文件不复制）。</summary>
        public Dictionary<string, List<string>> SharedWith { get; set; }
        public List<InvoiceRec> Invoices { get; set; }
        /// <summary>从项目里移除、但文件保留的资料（相对路径），扫描时跳过。</summary>
        public List<string> Hidden { get; set; }
        /// <summary>不再从中拆发票的页（“来源文件|页码”）：拆出的发票被换成重开的新票或被移除后，下次拆分不再拆回旧票。</summary>
        public List<string> SkipInvoicePages { get; set; }
        public Library()
        {
            Version = 1; Root = ""; Updated = ""; BackupDir = "";
            Excludes = new List<string>(); Projects = new List<Project>(); Files = new List<FileRec>();
            FileProject = new Dictionary<string, string>(); FileCat = new Dictionary<string, string>();
            FillFields = new List<string>(); TextTried = new Dictionary<string, string>(); SharedWith = new Dictionary<string, List<string>>(); Invoices = new List<InvoiceRec>(); Hidden = new List<string>(); SkipInvoicePages = new List<string>();
        }
    }

    public class Hit
    {
        public FileRec File;
        public int Score;
        public string Snippet;
    }

    public class ProjectInfo
    {
        public int FileCount;
        public Dictionary<string, int> CatCount = new Dictionary<string, int>();
        public List<string> Missing = new List<string>();
        public string Stage = "";
        public int StageIndex = -1;
        public double ContractTotal, PaidTotal, UnpaidTotal;
    }
}
