using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class AssistantWindow : SingletonImGuiWindow<AssistantWindow>
    {
        private GeneralTabContent _generalTab;
        private AgentsTabContent _agentsTab;
        private OrganizerTabContent _organizerTab;
        private FiltersTabContent _filtersTab;
        private ItemDatabaseTabContent _itemDatabaseTab;
        private MacrosTabContent _macrosTab;
        private SkillsTabContent _skillsTab;
        private OpilandTabContent _opilandTab;

        private AssistantWindow() : base("Legion Assistant")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;

            _generalTab = new GeneralTabContent();
            _agentsTab = new AgentsTabContent();
            _organizerTab = new OrganizerTabContent();
            _filtersTab = new FiltersTabContent();
            _itemDatabaseTab = new ItemDatabaseTabContent();
            _macrosTab = new MacrosTabContent();
            _skillsTab = new SkillsTabContent();
            _opilandTab = new OpilandTabContent();
        }

        public override void DrawContent()
        {
            // Draw tab bar
            if (ImGui.BeginTabBar("TabMenuTabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    _generalTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Agents"))
                {
                    _agentsTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Organizer"))
                {
                    _organizerTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Filters"))
                {
                    _filtersTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Item Database"))
                {
                    _itemDatabaseTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Macros"))
                {
                    _macrosTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Skills"))
                {
                    _skillsTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Opiland"))
                {
                    _opilandTab.DrawContent();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        public override void Update()
        {
            base.Update();
            _macrosTab?.Update();
        }

        public override void Dispose()
        {
            _generalTab?.Dispose();
            _agentsTab?.Dispose();
            _organizerTab?.Dispose();
            _filtersTab?.Dispose();
            _itemDatabaseTab?.Dispose();
            _macrosTab?.Dispose();
            _skillsTab?.Dispose();
            _opilandTab?.Dispose();
            base.Dispose();
        }
    }
}
