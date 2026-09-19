using System;
using System.Collections.Generic;

namespace NextDesign.Extension { }
namespace NextDesign.Core
{
    public interface IProject { string Id { get; } string Path { get; } IModel GetModelById(string id); }
    public class Project : IProject
    {
        public string ProjectId = Guid.NewGuid().ToString();
        public string FilePath = @"C:\fictional\project.nd";
        public string Id { get { return ProjectId; } }
        public string Path { get { return FilePath; } }
        public Dictionary<string, IModel> Models = new Dictionary<string, IModel>();
        public IModel GetModelById(string id) { IModel model; return Models.TryGetValue(id, out model) ? model : null; }
    }
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
    public interface IInteraction : IModel
    {
        IEnumerable<IModel> Messages { get; }
        IEnumerable<IModel> Lifelines { get; }
    }
    public class Interaction : Model, IInteraction
    {
        public List<IModel> MessageList = new List<IModel>();
        public List<IModel> LifelineList = new List<IModel>();
        public IEnumerable<IModel> Messages { get { return MessageList; } }
        public IEnumerable<IModel> Lifelines { get { return LifelineList; } }
    }
    public class Model : IModel
    {
        public string ModelId = "fictional-id";
        public string Id { get { return ModelId; } }
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
