using FamilyLists.App.Views;

namespace FamilyLists.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("month", typeof(MonthPage));
        Routing.RegisterRoute("todo", typeof(TodoPage));
        Routing.RegisterRoute("expenses", typeof(ExpensesPage));
        Routing.RegisterRoute("item", typeof(ItemPage));
        Routing.RegisterRoute("bill", typeof(BillPage));
        Routing.RegisterRoute("spending", typeof(SpendingPage));
        Routing.RegisterRoute("join", typeof(JoinPage));
        Routing.RegisterRoute("import", typeof(ImportPage));
    }
}
