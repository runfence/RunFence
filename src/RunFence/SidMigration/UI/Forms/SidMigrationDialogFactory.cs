using RunFence.SidMigration.UI;

namespace RunFence.SidMigration.UI.Forms;

public sealed class SidMigrationDialogFactory(SidMigrationWorkflowFactory workflowFactory)
{
    public SidMigrationDialog Create()
    {
        return new SidMigrationDialog(workflowFactory.Create());
    }
}
