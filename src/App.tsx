import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, useAuth } from "./lib/auth";
import { isConfigured } from "./lib/supabase";
import { PageSpinner } from "./components/ui";
import Login from "./pages/Login";
import Home from "./pages/Home";
import JoinGroup from "./pages/JoinGroup";
import GroupHome from "./pages/GroupHome";
import GroupSettings from "./pages/GroupSettings";
import ListPage from "./pages/ListPage";
import BillReview from "./pages/BillReview";
import Spending from "./pages/Spending";
import SetupNeeded from "./pages/SetupNeeded";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: true },
  },
});

function Routed() {
  const { session, loading } = useAuth();
  if (loading) return <PageSpinner />;
  if (!session) return <Login />;
  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/join/:code" element={<JoinGroup />} />
      <Route path="/g/:groupId" element={<GroupHome />} />
      <Route path="/g/:groupId/settings" element={<GroupSettings />} />
      <Route path="/g/:groupId/l/:listId" element={<ListPage />} />
      <Route path="/g/:groupId/l/:listId/spending" element={<Spending />} />
      <Route path="/g/:groupId/bills/:billId" element={<BillReview />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  if (!isConfigured) return <SetupNeeded />;
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routed />
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
