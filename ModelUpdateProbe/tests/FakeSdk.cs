using System;
using System.Collections.Generic;

namespace NextDesign.Extension { }
namespace NextDesign.Core
{
    public interface IProject { }
    public class Project : IProject { }
    public class IField
    {
        public string Name = "Description", Type = "String";
        public int UpperBound = 1;
        public bool IsEmbedded, IsReference;
        public object TypeClass, TypeEnum;
    }
    public class Meta
    {
        public List<IField> Fields = new List<IField> { new IField() };
        public IEnumerable<IField> GetFields() { return Fields; }
    }
    public interface IModel
    {
        string Id { get; }
        string Name { get; }
        bool IsDeleted { get; }
        bool IsProxy { get; }
        bool IsEditable { get; }
        Meta Metaclass { get; }
        object GetField(string field);
        void SetField(string field, object value);
    }
    public class Model : IModel
    {
        public string Id { get { return "fictional-id"; } }
        public string Name { get { return "SECRET_MODEL_NAME"; } }
        public bool IsDeleted { get; set; }
        public bool IsProxy { get; set; }
        public bool IsEditable { get; set; }
        public Meta Metaclass { get; set; }
        public string Value = "SECRET_BEFORE_VALUE";
        public int Writes;
        public bool ThrowAfterWrite, FailRead;
        public Action OnWrite;
        public Model() { IsEditable = true; Metaclass = new Meta(); }
        public object GetField(string field) { if (FailRead) throw new InvalidOperationException("fake read failed"); return Value; }
        public void SetField(string field, object value)
        {
            Writes++; Value = (string)value;
            if (OnWrite != null) OnWrite();
            if (ThrowAfterWrite) throw new InvalidOperationException("fake partial failure");
        }
    }
}
namespace NextDesign.Desktop
{
    public class ICommandContext { public IApplication App; }
    public class ICommandParams { }
    public class Workspace
    {
        public NextDesign.Core.IProject CurrentProject;
        public NextDesign.Core.IModel CurrentModel;
    }
    public class UI
    {
        public bool Confirm = true;
        public string Folder, Last;
        public Action OnConfirm;
        public bool ShowConfirmDialog(string message, string title) { if (OnConfirm != null) OnConfirm(); return Confirm; }
        public void ShowInformationDialog(string message, string title) { Last = message; }
        public string ShowSelectFolderDialog(string title) { return Folder; }
    }
    public class Window { public UI UI = new UI(); }
    public class IApplication { public Workspace Workspace = new Workspace(); public Window Window = new Window(); }
}
