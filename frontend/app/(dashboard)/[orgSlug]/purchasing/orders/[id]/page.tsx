"use client";

import { useParams } from "next/navigation";
import { OrderEditor } from "@/components/purchasing/order-editor";

export default function PurchaseOrderPage() {
  const { id } = useParams<{ id: string }>();
  return <OrderEditor key={id} id={id} />;
}
