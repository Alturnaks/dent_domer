"use client";

// Общие справочные запросы с кэшированием TanStack Query.
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import type { Chair, NamedRef, ServiceItem, Staff } from "@/lib/types";
import type { Schemas } from "@/lib/types";

export function useDoctors(branchId?: string | null) {
  return useQuery({
    queryKey: ["doctors", branchId ?? "all"],
    queryFn: () => api<Staff[]>("/doctors", { query: { branch_id: branchId ?? undefined } }),
    staleTime: 5 * 60_000,
  });
}

export function useServices(branchId?: string | null) {
  return useQuery({
    queryKey: ["services", branchId ?? "all"],
    queryFn: () => api<ServiceItem[]>("/services", { query: { branch_id: branchId ?? undefined } }),
    staleTime: 5 * 60_000,
  });
}

export function useServiceCategories() {
  return useQuery({
    queryKey: ["service-categories"],
    queryFn: () => api<Schemas["ServiceCategoryDto"][]>("/service-categories"),
    staleTime: 5 * 60_000,
  });
}

export function useChairs(branchId?: string | null) {
  return useQuery({
    queryKey: ["chairs", branchId ?? "all"],
    queryFn: () => api<Chair[]>("/chairs", { query: { branch_id: branchId ?? undefined } }),
    staleTime: 5 * 60_000,
  });
}

export function useReference(path: "/cancel-reasons" | "/lead-sources" | "/writeoff-reasons" | "/expense-categories") {
  return useQuery({ queryKey: ["ref", path], queryFn: () => api<NamedRef[]>(path), staleTime: 5 * 60_000 });
}

export function useBranches() {
  return useQuery({ queryKey: ["branches"], queryFn: () => api<Schemas["BranchDto"][]>("/branches"), staleTime: 5 * 60_000 });
}
