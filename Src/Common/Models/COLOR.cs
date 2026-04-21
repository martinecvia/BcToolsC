using System.Runtime.Serialization; // Keep for .NET 4.6

namespace BcToolsC.Models
{
    [DataContract]
    // https://stackoverflow.com/questions/19886047/custom-coloreditor-does-not-work-properly-on-color-struct
    // https://webhelp.micromine.com/mm/23.5/English/Content/mmtools/IDH_AUTOCAD_COLOUR_INDEX.htm
    public readonly struct COLOR
    {
        [DataMember] public readonly short ColorIndex;

        public static implicit operator COLOR(short colorIndex) => new COLOR(colorIndex);
        public static implicit operator short(COLOR color) => color.ColorIndex;
        private COLOR(short colorIndex) => ColorIndex = colorIndex;
    }
}